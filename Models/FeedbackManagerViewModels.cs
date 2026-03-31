using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using TilerFront.DatabaseModels;

namespace PostProcessingServer.Models
{
    public class FeedbackListViewModel
    {
        public List<FeedbackItemViewModel> Feedbacks { get; set; } = new List<FeedbackItemViewModel>();
        public PaginationViewModel Pagination { get; set; } = new PaginationViewModel();
        public string FilterCategory { get; set; }
        public string FilterStatus { get; set; }
        public string SearchQuery { get; set; }
    }

    public class FeedbackItemViewModel
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Category { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Status { get; set; }
        public string ExternalTicketUrl { get; set; }
        public string ExternalTicketId { get; set; }
        public string DeveloperNotes { get; set; }
        public string LinkedFeedbackId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        public string GetStatusBadgeClass()
        {
            switch (Status)
            {
                case "Open": return "bg-primary";
                case "Acknowledged": return "bg-info";
                case "InProgress": return "bg-warning text-dark";
                case "Resolved": return "bg-success";
                case "Closed": return "bg-secondary";
                default: return "bg-light text-dark";
            }
        }

        public string GetCategoryBadgeClass()
        {
            switch (Category)
            {
                case "Bug": return "bg-danger";
                case "Feature": return "bg-primary";
                case "Enhancement": return "bg-info";
                case "General": return "bg-secondary";
                default: return "bg-light text-dark";
            }
        }

        public string GetCategoryIcon()
        {
            switch (Category)
            {
                case "Bug": return "bi-bug-fill";
                case "Feature": return "bi-lightbulb-fill";
                case "Enhancement": return "bi-arrow-up-circle-fill";
                case "General": return "bi-chat-dots-fill";
                default: return "bi-question-circle";
            }
        }
    }

    public class FeedbackDetailViewModel
    {
        public FeedbackItemViewModel Feedback { get; set; }
        public List<FeedbackItemViewModel> LinkedFeedbacks { get; set; } = new List<FeedbackItemViewModel>();
    }

    public class FeedbackTriageModel
    {
        [Required]
        public string FeedbackId { get; set; }

        public string Status { get; set; }

        [MaxLength(512)]
        public string ExternalTicketUrl { get; set; }

        [MaxLength(128)]
        public string ExternalTicketId { get; set; }

        [MaxLength(2000)]
        public string DeveloperNotes { get; set; }

        [MaxLength(128)]
        public string LinkedFeedbackId { get; set; }
    }

    public class FeedbackLinkModel
    {
        [Required]
        public string FeedbackId { get; set; }

        [Required]
        public string LinkedFeedbackId { get; set; }
    }
}
