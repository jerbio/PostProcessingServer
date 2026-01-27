using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TilerElements;

namespace PostProcessingServer.Models
{
    /// <summary>
    /// ViewModel for Chat Audit login
    /// </summary>
    public class ChatAuditLoginViewModel
    {
        [Required(ErrorMessage = "Username is required")]
        [Display(Name = "Username")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; }

        public string ReturnUrl { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// ViewModel for searching anonymous users
    /// </summary>
    public class UserSearchViewModel
    {
        [Display(Name = "User ID")]
        public string UserId { get; set; }

        public UserSearchResultViewModel SearchResult { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// ViewModel for paginated list of anonymous users
    /// </summary>
    public class AnonymousUserListViewModel
    {
        public List<UserSearchResultViewModel> Users { get; set; }
        public PaginationViewModel Pagination { get; set; }

        public AnonymousUserListViewModel()
        {
            Users = new List<UserSearchResultViewModel>();
            Pagination = new PaginationViewModel();
        }
    }

    /// <summary>
    /// ViewModel for displaying anonymous user search results
    /// </summary>
    public class UserSearchResultViewModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public bool IsAnonymous { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public int SessionCount { get; set; }
        public string TimeZone { get; set; }
    }

    /// <summary>
    /// ViewModel for paginated list of chat sessions
    /// </summary>
    public class ChatSessionListViewModel
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public List<ChatSessionSummaryViewModel> Sessions { get; set; }
        public PaginationViewModel Pagination { get; set; }

        public ChatSessionListViewModel()
        {
            Sessions = new List<ChatSessionSummaryViewModel>();
            Pagination = new PaginationViewModel();
        }
    }

    /// <summary>
    /// ViewModel for a single session summary
    /// </summary>
    public class ChatSessionSummaryViewModel
    {
        public string SessionId { get; set; }
        public string SessionTitle { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int MessageCount { get; set; }
        public string Language { get; set; }
        public string LastMessagePreview { get; set; }
    }

    /// <summary>
    /// ViewModel for detailed chat view with messages
    /// </summary>
    public class ChatDetailViewModel
    {
        public string SessionId { get; set; }
        public string SessionTitle { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public DateTimeOffset SessionCreatedAt { get; set; }
        public string Language { get; set; }
        public List<ChatMessageViewModel> Messages { get; set; }
        public PaginationViewModel Pagination { get; set; }

        public ChatDetailViewModel()
        {
            Messages = new List<ChatMessageViewModel>();
            Pagination = new PaginationViewModel();
        }
    }

    /// <summary>
    /// ViewModel for a single chat message/prompt
    /// </summary>
    public class ChatMessageViewModel
    {
        public string PromptId { get; set; }
        public string Content { get; set; }
        public string Origin { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int Index { get; set; }
        public bool? IsActionable { get; set; }
        public string RequestId { get; set; }

        /// <summary>
        /// Returns CSS class based on message origin for styling
        /// </summary>
        public string GetMessageCssClass()
        {
            switch (Origin?.ToLower())
            {
                case "user":
                    return "message-user";
                case "tiler":
                    return "message-tiler";
                case "autoprompt":
                    return "message-auto";
                default:
                    return "message-system";
            }
        }

        /// <summary>
        /// Returns display name for the message sender
        /// </summary>
        public string GetSenderDisplayName()
        {
            switch (Origin?.ToLower())
            {
                case "user":
                    return "User";
                case "tiler":
                    return "Tiler";
                case "autoprompt":
                    return "Auto";
                default:
                    return "System";
            }
        }

        /// <summary>
        /// Returns icon class based on message origin
        /// </summary>
        public string GetSenderIcon()
        {
            switch (Origin?.ToLower())
            {
                case "user":
                    return "bi-person-fill";
                case "tiler":
                    return "bi-robot";
                case "autoprompt":
                    return "bi-lightning-fill";
                default:
                    return "bi-gear-fill";
            }
        }
    }

    /// <summary>
    /// ViewModel for pagination controls
    /// </summary>
    public class PaginationViewModel
    {
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalItems { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;

        /// <summary>
        /// Gets the range of page numbers to display
        /// </summary>
        public IEnumerable<int> GetPageRange(int maxPages = 5)
        {
            int startPage = Math.Max(1, CurrentPage - maxPages / 2);
            int endPage = Math.Min(TotalPages, startPage + maxPages - 1);
            
            // Adjust start if we're near the end
            if (endPage - startPage < maxPages - 1)
            {
                startPage = Math.Max(1, endPage - maxPages + 1);
            }

            for (int i = startPage; i <= endPage; i++)
            {
                yield return i;
            }
        }
    }
}
