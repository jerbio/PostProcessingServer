using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using PostProcessingServer.Filters;
using PostProcessingServer.Models;
using PostProcessingServer.Services;

namespace PostProcessingServer.Controllers
{
    /// <summary>
    /// Controller for auditing chat history of anonymous users.
    /// Provides read-only access to view chat sessions and messages.
    /// Requires authentication via credentials stored in web.config.
    /// </summary>
    [ChatAuditAuthorize]
    public class ChatAuditController : Controller
    {
        private readonly IChatAuditService _chatAuditService;

        public ChatAuditController()
        {
            _chatAuditService = new ChatAuditService();
        }

        public ChatAuditController(IChatAuditService chatAuditService)
        {
            _chatAuditService = chatAuditService;
        }

        #region Authentication Actions

        /// <summary>
        /// GET: /ChatAudit/Login
        /// Displays the login page
        /// </summary>
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            // If already authenticated, redirect to index
            if (ChatAuditAuthorizeAttribute.IsAuthenticated(HttpContext))
            {
                return RedirectToAction("Index");
            }

            return View(new ChatAuditLoginViewModel { ReturnUrl = returnUrl });
        }

        /// <summary>
        /// POST: /ChatAudit/Login
        /// Processes login credentials
        /// </summary>
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Login(ChatAuditLoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (ChatAuditAuthorizeAttribute.ValidateCredentials(model.Username, model.Password))
            {
                ChatAuditAuthorizeAttribute.SignIn(HttpContext, model.Username);

                // Redirect to return URL or default to Index
                if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                {
                    return Redirect(model.ReturnUrl);
                }

                return RedirectToAction("Index");
            }

            model.ErrorMessage = "Invalid username or password.";
            return View(model);
        }

        /// <summary>
        /// POST: /ChatAudit/Logout
        /// Logs out the current user
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            ChatAuditAuthorizeAttribute.SignOut(HttpContext);
            return RedirectToAction("Login");
        }

        #endregion

        #region Audit Actions

        /// <summary>
        /// GET: /ChatAudit
        /// Displays the user search page
        /// </summary>
        public ActionResult Index()
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            return View(new UserSearchViewModel());
        }

        /// <summary>
        /// GET: /ChatAudit/Users
        /// Displays paginated list of all anonymous users
        /// </summary>
        public async Task<ActionResult> Users(int page = 1)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            try
            {
                var usersViewModel = await _chatAuditService.GetAnonymousUsersAsync(page);
                return View(usersViewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error loading anonymous users: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading users.";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// POST: /ChatAudit/Search
        /// Searches for an anonymous user by ID
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Search(UserSearchViewModel model)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            if (string.IsNullOrWhiteSpace(model.UserId))
            {
                model.ErrorMessage = "Please enter a User ID to search.";
                return View("Index", model);
            }

            try
            {
                var searchResult = await _chatAuditService.SearchAnonymousUserAsync(model.UserId.Trim());

                if (searchResult == null)
                {
                    model.ErrorMessage = "No anonymous user found with the specified ID. Note: Only anonymous users can be searched for audit purposes.";
                    return View("Index", model);
                }

                model.SearchResult = searchResult;
                return View("Index", model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error searching for user: {ex.Message}");
                model.ErrorMessage = "An error occurred while searching. Please try again.";
                return View("Index", model);
            }
        }

        /// <summary>
        /// GET: /ChatAudit/Sessions/{userId}
        /// Displays paginated list of chat sessions for a user
        /// </summary>
        public async Task<ActionResult> Sessions(string id, int page = 1)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            if (string.IsNullOrWhiteSpace(id))
            {
                return RedirectToAction("Index");
            }

            try
            {
                var sessionsViewModel = await _chatAuditService.GetUserSessionsAsync(id, page);

                if (sessionsViewModel == null)
                {
                    TempData["ErrorMessage"] = "User not found or is not an anonymous user.";
                    return RedirectToAction("Index");
                }

                return View(sessionsViewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error loading sessions: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading sessions.";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// GET: /ChatAudit/Chat/{sessionId}
        /// Displays the chat detail view with paginated messages
        /// </summary>
        public async Task<ActionResult> Chat(string id, int page = 1)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            if (string.IsNullOrWhiteSpace(id))
            {
                return RedirectToAction("Index");
            }

            try
            {
                var chatDetailViewModel = await _chatAuditService.GetSessionChatDetailAsync(id, page);

                if (chatDetailViewModel == null)
                {
                    TempData["ErrorMessage"] = "Session not found or does not belong to an anonymous user.";
                    return RedirectToAction("Index");
                }

                return View(chatDetailViewModel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error loading chat: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading the chat.";
                return RedirectToAction("Index");
            }
        }

        #endregion
    }
}
