using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using PostProcessingServer.Models;
using TilerElements;

namespace PostProcessingServer.Services
{
    /// <summary>
    /// Service implementation for chat audit functionality.
    /// Provides read-only access to anonymous user chat history for auditing purposes.
    /// </summary>
    public class ChatAuditService : IChatAuditService
    {
        /// <summary>
        /// Gets a paginated list of all anonymous users
        /// </summary>
        public async Task<AnonymousUserListViewModel> GetAnonymousUsersAsync(int page = 1, int pageSize = 20)
        {
            // Ensure valid pagination parameters
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(100, pageSize));

            using (var db = new PostProcessorApplicationDbContext())
            {
                // Get total count of anonymous users
                var totalUsers = await db.Users
                    .CountAsync(u => u.IsAnonymous_DB == true)
                    .ConfigureAwait(false);

                // Get paginated anonymous users with session counts using LEFT JOIN
                var usersWithCounts = await (
                    from u in db.Users
                    where u.IsAnonymous_DB == true
                    join s in db.VibeSessions on u.Id equals s.TilerUserId into sessions
                    orderby u.CreationTime_DB descending
                    select new
                    {
                        User = u,
                        SessionCount = sessions.Count()
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var userViewModels = usersWithCounts.Select(x => new UserSearchResultViewModel
                {
                    UserId = x.User.Id,
                    UserName = x.User.UserName ?? "Anonymous",
                    IsAnonymous = x.User.IsAnonymous_DB ?? false,
                    CreatedAt = x.User.CreationTime_DB.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(x.User.CreationTime_DB.Value)
                        : (DateTimeOffset?)null,
                    SessionCount = x.SessionCount,
                    TimeZone = x.User.TimeZone
                }).ToList();

                return new AnonymousUserListViewModel
                {
                    Users = userViewModels,
                    Pagination = new PaginationViewModel
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = totalUsers
                    }
                };
            }
        }

        /// <summary>
        /// Searches for an anonymous user by their ID
        /// </summary>
        public async Task<UserSearchResultViewModel> SearchAnonymousUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            using (var db = new PostProcessorApplicationDbContext())
            {
                // Get user with session count using LEFT JOIN in single query
                var result = await (
                    from u in db.Users
                    where u.Id == userId && u.IsAnonymous_DB == true
                    join s in db.VibeSessions on u.Id equals s.TilerUserId into sessions
                    select new
                    {
                        User = u,
                        SessionCount = sessions.Count()
                    })
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (result == null)
                {
                    return null;
                }

                return new UserSearchResultViewModel
                {
                    UserId = result.User.Id,
                    UserName = result.User.UserName ?? "Anonymous",
                    IsAnonymous = result.User.IsAnonymous_DB ?? false,
                    CreatedAt = result.User.CreationTime_DB.HasValue 
                        ? DateTimeOffset.FromUnixTimeMilliseconds(result.User.CreationTime_DB.Value) 
                        : (DateTimeOffset?)null,
                    SessionCount = result.SessionCount,
                    TimeZone = result.User.TimeZone
                };
            }
        }

        /// <summary>
        /// Gets paginated list of chat sessions for an anonymous user
        /// </summary>
        public async Task<ChatSessionListViewModel> GetUserSessionsAsync(string userId, int page = 1, int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            // Ensure valid pagination parameters
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(100, pageSize));

            using (var db = new PostProcessorApplicationDbContext())
            {
                // Verify user exists and is anonymous
                var user = await db.Users
                    .Where(u => u.Id == userId && u.IsAnonymous_DB == true)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (user == null)
                {
                    return null;
                }

                // Get total count for pagination
                var totalSessions = await db.VibeSessions
                    .CountAsync(s => s.TilerUserId == userId)
                    .ConfigureAwait(false);

                // Get paginated sessions with message counts and last message using LEFT JOINs
                var sessionsWithData = await (
                    from s in db.VibeSessions
                    where s.TilerUserId == userId
                    join p in db.VibePrompts on s.Id equals p.SessionId into prompts
                    orderby s.CreationTimeInMs descending
                    select new
                    {
                        Session = s,
                        MessageCount = prompts.Count(),
                        LastMessagePreview = prompts
                            .OrderByDescending(m => m.CreationTimeInMs)
                            .Select(m => m.Prompt)
                            .FirstOrDefault()
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var sessionViewModels = sessionsWithData.Select(x => new ChatSessionSummaryViewModel
                {
                    SessionId = x.Session.Id,
                    SessionTitle = x.Session.SessionTitle ?? "Untitled Session",
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(x.Session.CreationTimeInMs),
                    MessageCount = x.MessageCount,
                    Language = x.Session.Language ?? "en",
                    LastMessagePreview = TruncateString(x.LastMessagePreview, 100)
                }).ToList();

                return new ChatSessionListViewModel
                {
                    UserId = userId,
                    UserName = user.UserName ?? "Anonymous",
                    Sessions = sessionViewModels,
                    Pagination = new PaginationViewModel
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = totalSessions
                    }
                };
            }
        }

        /// <summary>
        /// Gets detailed chat view with paginated messages for a session
        /// </summary>
        public async Task<ChatDetailViewModel> GetSessionChatDetailAsync(string sessionId, int page = 1, int pageSize = 50)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            // Ensure valid pagination parameters
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(200, pageSize));

            using (var db = new PostProcessorApplicationDbContext())
            {
                // Get session with user and message count using JOIN in single query
                var sessionData = await (
                    from s in db.VibeSessions
                    join u in db.Users on s.TilerUserId equals u.Id
                    where s.Id == sessionId && u.IsAnonymous_DB == true
                    join p in db.VibePrompts on s.Id equals p.SessionId into prompts
                    select new
                    {
                        Session = s,
                        User = u,
                        TotalMessages = prompts.Count()
                    })
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (sessionData == null)
                {
                    return null;
                }

                // Get paginated messages ordered by creation time (oldest first for conversation flow)
                var messages = await db.VibePrompts
                    .Where(p => p.SessionId == sessionId)
                    .OrderBy(p => p.CreationTimeInMs)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var messageViewModels = messages.Select(m => new ChatMessageViewModel
                {
                    PromptId = m.Id,
                    Content = m.Prompt,
                    Origin = m.Origin.ToString(),
                    // VibePrompt.CreationTimeInMs stores UTC ticks, not milliseconds
                    CreatedAt = new DateTimeOffset(m.CreationTimeInMs, TimeSpan.Zero),
                    Index = m.Index,
                    IsActionable = m.ActionablFlag,
                    RequestId = m.VibeRequestId
                }).ToList();

                return new ChatDetailViewModel
                {
                    SessionId = sessionData.Session.Id,
                    SessionTitle = sessionData.Session.SessionTitle ?? "Untitled Session",
                    UserId = sessionData.Session.TilerUserId,
                    UserName = sessionData.User?.UserName ?? "Anonymous",
                    SessionCreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(sessionData.Session.CreationTimeInMs),
                    Language = sessionData.Session.Language ?? "en",
                    Messages = messageViewModels,
                    Pagination = new PaginationViewModel
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = sessionData.TotalMessages
                    }
                };
            }
        }

        /// <summary>
        /// Validates that a session belongs to an anonymous user
        /// </summary>
        public async Task<bool> IsSessionFromAnonymousUserAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return false;
            }

            using (var db = new PostProcessorApplicationDbContext())
            {
                // Use JOIN instead of Include for explicit join
                return await (
                    from s in db.VibeSessions
                    join u in db.Users on s.TilerUserId equals u.Id
                    where s.Id == sessionId && u.IsAnonymous_DB == true
                    select s.Id)
                    .AnyAsync()
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Helper method to truncate strings for preview
        /// </summary>
        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }
    }
}
