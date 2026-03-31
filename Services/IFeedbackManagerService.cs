using System.Collections.Generic;
using System.Threading.Tasks;
using PostProcessingServer.Models;

namespace PostProcessingServer.Services
{
    public interface IFeedbackManagerService
    {
        Task<FeedbackListViewModel> GetFeedbacksAsync(string filterCategory = null, string filterStatus = null, string searchQuery = null, int page = 1, int pageSize = 20);
        Task<FeedbackDetailViewModel> GetFeedbackDetailAsync(string feedbackId);
        Task<bool> UpdateTriageAsync(FeedbackTriageModel triageModel);
        Task<bool> LinkFeedbacksAsync(string feedbackId, string linkedFeedbackId);
        Task<bool> UnlinkFeedbackAsync(string feedbackId);
        Task<List<FeedbackItemViewModel>> SearchFeedbacksByTitleAsync(string query);
    }
}
