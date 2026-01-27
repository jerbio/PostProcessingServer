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

                // Get paginated anonymous users ordered by creation time (newest first)
                var users = await db.Users
                    .Where(u => u.IsAnonymous_DB == true)
                    .OrderByDescending(u => u.CreationTime_DB)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Get session counts for each user
                var userIds = users.Select(u => u.Id).ToList();
                var sessionCounts = await db.VibeSessions
                    .Where(s => userIds.Contains(s.TilerUserId))
                    .GroupBy(s => s.TilerUserId)
                    .Select(g => new { UserId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.UserId, x => x.Count)
                    .ConfigureAwait(false);

                var userViewModels = users.Select(u => new UserSearchResultViewModel
                {
                    UserId = u.Id,
                    UserName = u.UserName ?? "Anonymous",
                    IsAnonymous = u.IsAnonymous_DB ?? false,
                    CreatedAt = u.CreationTime_DB.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(u.CreationTime_DB.Value)
                        : (DateTimeOffset?)null,
                    SessionCount = sessionCounts.ContainsKey(u.Id) ? sessionCounts[u.Id] : 0,
                    TimeZone = u.TimeZone
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
                var user = await db.Users
                    .Where(u => u.Id == userId
                    && u.IsAnonymous_DB == true
                    )
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (user == null)
                {
                    return null;
                }

                // Get session count for this user
                var sessionCount = await db.VibeSessions
                    .CountAsync(s => s.TilerUserId == userId)
                    .ConfigureAwait(false);

                return new UserSearchResultViewModel
                {
                    UserId = user.Id,
                    UserName = user.UserName ?? "Anonymous",
                    IsAnonymous = user.IsAnonymous_DB ?? false,
                    CreatedAt = user.CreationTime_DB.HasValue 
                        ? DateTimeOffset.FromUnixTimeMilliseconds(user.CreationTime_DB.Value) 
                        : (DateTimeOffset?)null,
                    SessionCount = sessionCount,
                    TimeZone = user.TimeZone
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

                // Get paginated sessions ordered by creation time (newest first)
                var sessions = await db.VibeSessions
                    .Where(s => s.TilerUserId == userId)
                    .OrderByDescending(s => s.CreationTimeInMs)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                // Get message counts for each session
                var sessionIds = sessions.Select(s => s.Id).ToList();
                var messageCounts = await db.VibePrompts
                    .Where(p => sessionIds.Contains(p.SessionId))
                    .GroupBy(p => p.SessionId)
                    .Select(g => new { SessionId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.SessionId, x => x.Count)
                    .ConfigureAwait(false);

                // Get last message preview for each session
                var lastMessages = await db.VibePrompts
                    .Where(p => sessionIds.Contains(p.SessionId))
                    .GroupBy(p => p.SessionId)
                    .Select(g => g.OrderByDescending(p => p.CreationTimeInMs).FirstOrDefault())
                    .ToDictionaryAsync(p => p.SessionId, p => p.Prompt)
                    .ConfigureAwait(false);

                var sessionViewModels = sessions.Select(s => new ChatSessionSummaryViewModel
                {
                    SessionId = s.Id,
                    SessionTitle = s.SessionTitle ?? "Untitled Session",
                    CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(s.CreationTimeInMs),
                    MessageCount = messageCounts.ContainsKey(s.Id) ? messageCounts[s.Id] : 0,
                    Language = s.Language ?? "en",
                    LastMessagePreview = lastMessages.ContainsKey(s.Id) 
                        ? TruncateString(lastMessages[s.Id], 100) 
                        : null
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
                // Get session with user verification (must be anonymous)
                var session = await db.VibeSessions
                    .Include(s => s.TilerUser)
                    .Where(s => s.Id == sessionId && s.TilerUser.IsAnonymous_DB == true)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);

                if (session == null)
                {
                    return null;
                }

                // Get total message count for pagination
                var totalMessages = await db.VibePrompts
                    .CountAsync(p => p.SessionId == sessionId)
                    .ConfigureAwait(false);

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
                    SessionId = session.Id,
                    SessionTitle = session.SessionTitle ?? "Untitled Session",
                    UserId = session.TilerUserId,
                    UserName = session.TilerUser?.UserName ?? "Anonymous",
                    SessionCreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(session.CreationTimeInMs),
                    Language = session.Language ?? "en",
                    Messages = messageViewModels,
                    Pagination = new PaginationViewModel
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = totalMessages
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
                return await db.VibeSessions
                    .Include(s => s.TilerUser)
                    .AnyAsync(s => s.Id == sessionId && s.TilerUser.IsAnonymous_DB == true)
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
