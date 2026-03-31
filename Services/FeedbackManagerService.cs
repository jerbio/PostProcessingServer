using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using PostProcessingServer.Models;
using TilerElements;
using TilerFront.DatabaseModels;

namespace PostProcessingServer.Services
{
    public class FeedbackManagerService : IFeedbackManagerService
    {
        public async Task<FeedbackListViewModel> GetFeedbacksAsync(string filterCategory = null, string filterStatus = null, string searchQuery = null, int page = 1, int pageSize = 20)
        {
            page = Math.Max(1, page);
            pageSize = Math.Max(1, Math.Min(100, pageSize));

            using (var db = new PostProcessorApplicationDbContext())
            {
                IQueryable<UserFeedback> query = db.UserFeedbacks;

                if (!string.IsNullOrWhiteSpace(filterCategory))
                {
                    query = query.Where(f => f.CategoryName == filterCategory);
                }

                if (!string.IsNullOrWhiteSpace(filterStatus))
                {
                    query = query.Where(f => f.StatusName == filterStatus);
                }

                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    query = query.Where(f => f.Title.Contains(searchQuery) || f.Description.Contains(searchQuery));
                }

                int totalItems = await query.CountAsync().ConfigureAwait(false);

                var feedbacks = await query
                    .OrderByDescending(f => f.CreatedAtUtcEpoch)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var userIds = feedbacks.Select(f => f.UserId).Distinct().ToList();
                var users = await db.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.UserName)
                    .ConfigureAwait(false);

                return new FeedbackListViewModel
                {
                    Feedbacks = feedbacks.Select(f => ToViewModel(f, users)).ToList(),
                    Pagination = new PaginationViewModel
                    {
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = totalItems
                    },
                    FilterCategory = filterCategory,
                    FilterStatus = filterStatus,
                    SearchQuery = searchQuery
                };
            }
        }

        public async Task<FeedbackDetailViewModel> GetFeedbackDetailAsync(string feedbackId)
        {
            using (var db = new PostProcessorApplicationDbContext())
            {
                var feedback = await db.UserFeedbacks
                    .FirstOrDefaultAsync(f => f.Id == feedbackId)
                    .ConfigureAwait(false);

                if (feedback == null) return null;

                var user = await db.Users
                    .FirstOrDefaultAsync(u => u.Id == feedback.UserId)
                    .ConfigureAwait(false);

                var userLookup = new Dictionary<string, string>();
                if (user != null) userLookup[user.Id] = user.UserName;

                var result = new FeedbackDetailViewModel
                {
                    Feedback = ToViewModel(feedback, userLookup)
                };

                // Find feedbacks linked to this one (bidirectional)
                var linkedFeedbacks = await db.UserFeedbacks
                    .Where(f => f.LinkedFeedbackId == feedbackId || (feedback.LinkedFeedbackId != null && f.Id == feedback.LinkedFeedbackId))
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (linkedFeedbacks.Any())
                {
                    var linkedUserIds = linkedFeedbacks.Select(f => f.UserId).Distinct().Where(id => !userLookup.ContainsKey(id)).ToList();
                    if (linkedUserIds.Any())
                    {
                        var linkedUsers = await db.Users
                            .Where(u => linkedUserIds.Contains(u.Id))
                            .ToDictionaryAsync(u => u.Id, u => u.UserName)
                            .ConfigureAwait(false);
                        foreach (var kv in linkedUsers) userLookup[kv.Key] = kv.Value;
                    }

                    result.LinkedFeedbacks = linkedFeedbacks.Select(f => ToViewModel(f, userLookup)).ToList();
                }

                return result;
            }
        }

        public async Task<bool> UpdateTriageAsync(FeedbackTriageModel triageModel)
        {
            using (var db = new PostProcessorApplicationDbContext())
            {
                var feedback = await db.UserFeedbacks
                    .FirstOrDefaultAsync(f => f.Id == triageModel.FeedbackId)
                    .ConfigureAwait(false);

                if (feedback == null) return false;

                if (!string.IsNullOrWhiteSpace(triageModel.Status))
                {
                    FeedbackStatus status;
                    if (Enum.TryParse(triageModel.Status, true, out status))
                    {
                        feedback.Status = status;
                    }
                }

                if (triageModel.ExternalTicketUrl != null)
                {
                    feedback.ExternalTicketUrl = triageModel.ExternalTicketUrl;
                }

                if (triageModel.ExternalTicketId != null)
                {
                    feedback.ExternalTicketId = triageModel.ExternalTicketId;
                }

                if (triageModel.DeveloperNotes != null)
                {
                    feedback.DeveloperNotes = triageModel.DeveloperNotes;
                }

                if (triageModel.LinkedFeedbackId != null)
                {
                    feedback.LinkedFeedbackId = triageModel.LinkedFeedbackId;
                }

                feedback.UpdatedAtUtcEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<bool> LinkFeedbacksAsync(string feedbackId, string linkedFeedbackId)
        {
            using (var db = new PostProcessorApplicationDbContext())
            {
                var feedback = await db.UserFeedbacks
                    .FirstOrDefaultAsync(f => f.Id == feedbackId)
                    .ConfigureAwait(false);

                var linkedFeedback = await db.UserFeedbacks
                    .FirstOrDefaultAsync(f => f.Id == linkedFeedbackId)
                    .ConfigureAwait(false);

                if (feedback == null || linkedFeedback == null) return false;

                feedback.LinkedFeedbackId = linkedFeedbackId;
                feedback.UpdatedAtUtcEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<bool> UnlinkFeedbackAsync(string feedbackId)
        {
            using (var db = new PostProcessorApplicationDbContext())
            {
                var feedback = await db.UserFeedbacks
                    .FirstOrDefaultAsync(f => f.Id == feedbackId)
                    .ConfigureAwait(false);

                if (feedback == null) return false;

                feedback.LinkedFeedbackId = null;
                feedback.UpdatedAtUtcEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<List<FeedbackItemViewModel>> SearchFeedbacksByTitleAsync(string query)
        {
            using (var db = new PostProcessorApplicationDbContext())
            {
                var feedbacks = await db.UserFeedbacks
                    .Where(f => f.Title.Contains(query) || f.Id.Contains(query))
                    .OrderByDescending(f => f.CreatedAtUtcEpoch)
                    .Take(20)
                    .ToListAsync()
                    .ConfigureAwait(false);

                var userIds = feedbacks.Select(f => f.UserId).Distinct().ToList();
                var users = await db.Users
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.UserName)
                    .ConfigureAwait(false);

                return feedbacks.Select(f => ToViewModel(f, users)).ToList();
            }
        }

        private static FeedbackItemViewModel ToViewModel(UserFeedback feedback, Dictionary<string, string> userLookup)
        {
            string userName = "Unknown";
            if (userLookup != null && userLookup.ContainsKey(feedback.UserId))
            {
                userName = userLookup[feedback.UserId];
            }

            return new FeedbackItemViewModel
            {
                Id = feedback.Id,
                UserId = feedback.UserId,
                UserName = userName,
                Category = feedback.Category.ToString(),
                Title = feedback.Title,
                Description = feedback.Description,
                Status = feedback.Status.ToString(),
                ExternalTicketUrl = feedback.ExternalTicketUrl,
                ExternalTicketId = feedback.ExternalTicketId,
                DeveloperNotes = feedback.DeveloperNotes,
                LinkedFeedbackId = feedback.LinkedFeedbackId,
                CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(feedback.CreatedAtUtcEpoch),
                UpdatedAt = feedback.UpdatedAtUtcEpoch.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(feedback.UpdatedAtUtcEpoch.Value)
                    : (DateTimeOffset?)null
            };
        }
    }
}
