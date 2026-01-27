using System;
using System.Configuration;
using System.Web;
using System.Web.Mvc;

namespace PostProcessingServer.Filters
{
    /// <summary>
    /// Custom authorization filter for Chat Audit that validates against credentials in web.config
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class ChatAuditAuthorizeAttribute : AuthorizeAttribute
    {
        public const string SessionKey = "ChatAudit_Authenticated";
        public const string SessionUserKey = "ChatAudit_Username";

        protected override bool AuthorizeCore(HttpContextBase httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            // Check if user is authenticated via session
            var isAuthenticated = httpContext.Session[SessionKey] as bool?;
            return isAuthenticated == true;
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            // Redirect to login page
            filterContext.Result = new RedirectToRouteResult(
                new System.Web.Routing.RouteValueDictionary
                {
                    { "controller", "ChatAudit" },
                    { "action", "Login" },
                    { "returnUrl", filterContext.HttpContext.Request.RawUrl }
                });
        }

        /// <summary>
        /// Validates credentials against web.config settings
        /// Format in web.config: username:password;username2:password2
        /// </summary>
        public static bool ValidateCredentials(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            var credentialsConfig = ConfigurationManager.AppSettings["ChatAudit:Credentials"];

            if (string.IsNullOrEmpty(credentialsConfig))
            {
                System.Diagnostics.Trace.TraceWarning("ChatAudit:Credentials not configured in web.config");
                return false;
            }

            // Parse credentials: format is "username:password;username2:password2"
            var credentialPairs = credentialsConfig.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var pair in credentialPairs)
            {
                var parts = pair.Split(new[] { ':' }, 2); // Split on first colon only (password may contain colons)
                if (parts.Length == 2)
                {
                    var configUsername = parts[0].Trim();
                    var configPassword = parts[1].Trim();

                    if (string.Equals(username, configUsername, StringComparison.Ordinal) &&
                        string.Equals(password, configPassword, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Signs in the user by setting session values
        /// </summary>
        public static void SignIn(HttpContextBase httpContext, string username)
        {
            httpContext.Session[SessionKey] = true;
            httpContext.Session[SessionUserKey] = username;
        }

        /// <summary>
        /// Signs out the user by clearing session values
        /// </summary>
        public static void SignOut(HttpContextBase httpContext)
        {
            httpContext.Session.Remove(SessionKey);
            httpContext.Session.Remove(SessionUserKey);
        }

        /// <summary>
        /// Gets the currently authenticated username
        /// </summary>
        public static string GetCurrentUsername(HttpContextBase httpContext)
        {
            return httpContext.Session[SessionUserKey] as string;
        }

        /// <summary>
        /// Checks if the user is currently authenticated
        /// </summary>
        public static bool IsAuthenticated(HttpContextBase httpContext)
        {
            return httpContext.Session[SessionKey] as bool? == true;
        }
    }
}
