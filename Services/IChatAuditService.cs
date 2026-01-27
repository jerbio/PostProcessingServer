using System.Threading.Tasks;
using PostProcessingServer.Models;

namespace PostProcessingServer.Services
{
    /// <summary>
    /// Service interface for chat audit functionality.
    /// Provides read-only access to anonymous user chat history for auditing purposes.
    /// </summary>
    public interface IChatAuditService
    {
        /// <summary>
        /// Gets a paginated list of all anonymous users
        /// </summary>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated list of anonymous users</returns>
        Task<AnonymousUserListViewModel> GetAnonymousUsersAsync(int page = 1, int pageSize = 20);

        /// <summary>
        /// Searches for an anonymous user by their ID
        /// </summary>
        /// <param name="userId">The user ID to search for</param>
        /// <returns>User search result or null if not found or not anonymous</returns>
        Task<UserSearchResultViewModel> SearchAnonymousUserAsync(string userId);

        /// <summary>
        /// Gets paginated list of chat sessions for an anonymous user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated session list view model</returns>
        Task<ChatSessionListViewModel> GetUserSessionsAsync(string userId, int page = 1, int pageSize = 20);

        /// <summary>
        /// Gets detailed chat view with paginated messages for a session
        /// </summary>
        /// <param name="sessionId">The session ID</param>
        /// <param name="page">Page number (1-based)</param>
        /// <param name="pageSize">Number of messages per page</param>
        /// <returns>Chat detail view model or null if session not found or user is not anonymous</returns>
        Task<ChatDetailViewModel> GetSessionChatDetailAsync(string sessionId, int page = 1, int pageSize = 50);

        /// <summary>
        /// Validates that a session belongs to an anonymous user
        /// </summary>
        /// <param name="sessionId">The session ID to validate</param>
        /// <returns>True if session exists and belongs to an anonymous user</returns>
        Task<bool> IsSessionFromAnonymousUserAsync(string sessionId);
    }
}
