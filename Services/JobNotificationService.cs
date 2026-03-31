using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PostProcessingServer.Services
{
    /// <summary>
    /// Service for sending job completion notifications to TilerFront.
    /// This enables real-time WebSocket notifications to connected clients.
    /// </summary>
    public class JobNotificationService
    {
        private static readonly Lazy<JobNotificationService> _instance = 
            new Lazy<JobNotificationService>(() => new JobNotificationService());
        
        public static JobNotificationService Instance => _instance.Value;

        private readonly HttpClient _httpClient;
        private readonly string _tilerFrontBaseUrl;
        private readonly string _internalApiKey;
        private readonly bool _isEnabled;

        private JobNotificationService()
        {
            _tilerFrontBaseUrl = Utilities.ConfigurationUtility.GetString(
                Utilities.ConfigurationUtility.TilerFrontBaseUrlKey, 
                "https://localhost:44300"); // Default for local development
            
            _internalApiKey = Utilities.ConfigurationUtility.GetString(
                Utilities.ConfigurationUtility.InternalJobApiKeyKey);
            
            _isEnabled = Utilities.ConfigurationUtility.GetBool(
                Utilities.ConfigurationUtility.EnableJobNotificationsKey, 
                true); // Enabled by default

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            if (!string.IsNullOrWhiteSpace(_internalApiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Internal-Api-Key", _internalApiKey);
            }

            System.Diagnostics.Trace.WriteLine(
                $"JobNotificationService initialized: Enabled={_isEnabled}, BaseUrl={_tilerFrontBaseUrl}");
        }

        /// <summary>
        /// Sends a preview completion notification to TilerFront.
        /// TilerFront will then push a WebSocket notification to the connected user.
        /// </summary>
        /// <param name="userId">The TilerUser ID</param>
        /// <param name="vibeRequestId">The VibeRequest ID</param>
        /// <param name="previewId">The created VibePreview ID</param>
        /// <param name="scheduleDumpId">The Cosmos DB document ID</param>
        /// <param name="jobId">The original job ID</param>
        /// <returns>True if notification was sent successfully</returns>
        public async Task<bool> NotifyPreviewCompletedAsync(
            string userId, 
            string vibeRequestId, 
            string previewId, 
            string scheduleDumpId,
            string jobId = null)
        {
            if (!_isEnabled)
            {
                System.Diagnostics.Trace.WriteLine("Job notifications disabled - skipping preview notification");
                return true;
            }

            if (string.IsNullOrWhiteSpace(_internalApiKey))
            {
                System.Diagnostics.Trace.TraceWarning(
                    "InternalJobApiKey not configured - cannot send preview notification");
                return false;
            }

            try
            {
                var notification = new
                {
                    userId,
                    vibeRequestId,
                    previewId,
                    scheduleDumpId,
                    jobId
                };

                string json = JsonConvert.SerializeObject(notification);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string url = $"{_tilerFrontBaseUrl.TrimEnd('/')}/api/internal/jobs/preview-completed";
                
                System.Diagnostics.Trace.WriteLine($"Sending preview notification to {url} for user {userId}");
                
                HttpResponseMessage response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"Preview notification sent successfully for user {userId}, preview {previewId}");
                    return true;
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    System.Diagnostics.Trace.TraceWarning(
                        $"Preview notification failed with status {response.StatusCode}: {responseBody}");
                    return false;
                }
            }
            catch (TaskCanceledException)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Preview notification timed out for user {userId}");
                return false;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Trace.TraceWarning(
                    $"Preview notification HTTP error for user {userId}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    $"Unexpected error sending preview notification for user {userId}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Sends a generic job completion notification to TilerFront.
        /// </summary>
        public async Task<bool> NotifyJobCompletedAsync(
            string userId,
            string jobType,
            string jobId,
            string status,
            string vibeRequestId = null,
            string previewId = null,
            string scheduleDumpId = null,
            string errorMessage = null)
        {
            if (!_isEnabled)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_internalApiKey))
            {
                System.Diagnostics.Trace.TraceWarning(
                    "InternalJobApiKey not configured - cannot send job notification");
                return false;
            }

            try
            {
                var notification = new
                {
                    userId,
                    jobType,
                    jobId,
                    status,
                    vibeRequestId,
                    previewId,
                    scheduleDumpId,
                    errorMessage
                };

                string json = JsonConvert.SerializeObject(notification);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                string url = $"{_tilerFrontBaseUrl.TrimEnd('/')}/api/internal/jobs/completed";
                
                HttpResponseMessage response = await _httpClient.PostAsync(url, content).ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError(
                    $"Error sending job notification for user {userId}: {ex.Message}");
                return false;
            }
        }
    }
}
