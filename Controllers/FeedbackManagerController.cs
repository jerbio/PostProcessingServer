using System;
using System.Threading.Tasks;
using System.Web.Mvc;
using PostProcessingServer.Filters;
using PostProcessingServer.Models;
using PostProcessingServer.Services;

namespace PostProcessingServer.Controllers
{
    [ChatAuditAuthorize]
    public class FeedbackManagerController : Controller
    {
        private readonly IFeedbackManagerService _feedbackService;

        public FeedbackManagerController()
        {
            _feedbackService = new FeedbackManagerService();
        }

        public FeedbackManagerController(IFeedbackManagerService feedbackService)
        {
            _feedbackService = feedbackService;
        }

        /// <summary>
        /// GET: /FeedbackManager
        /// Displays paginated, filterable list of all user feedback
        /// </summary>
        public async Task<ActionResult> Index(string category, string status, string search, int page = 1)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            try
            {
                var model = await _feedbackService.GetFeedbacksAsync(category, status, search, page);
                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error loading feedbacks: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading feedback.";
                return View(new FeedbackListViewModel());
            }
        }

        /// <summary>
        /// GET: /FeedbackManager/Detail/{id}
        /// Shows full feedback detail with linked items
        /// </summary>
        public async Task<ActionResult> Detail(string id)
        {
            ViewBag.CurrentUser = ChatAuditAuthorizeAttribute.GetCurrentUsername(HttpContext);
            if (string.IsNullOrWhiteSpace(id))
            {
                return RedirectToAction("Index");
            }

            try
            {
                var model = await _feedbackService.GetFeedbackDetailAsync(id);
                if (model == null)
                {
                    TempData["ErrorMessage"] = "Feedback not found.";
                    return RedirectToAction("Index");
                }
                return View(model);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error loading feedback detail: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while loading feedback details.";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// POST: /FeedbackManager/Triage
        /// Updates status, ticket links, developer notes
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Triage(FeedbackTriageModel model)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.FeedbackId))
            {
                TempData["ErrorMessage"] = "Invalid triage data.";
                return RedirectToAction("Index");
            }

            try
            {
                var success = await _feedbackService.UpdateTriageAsync(model);
                if (success)
                {
                    TempData["SuccessMessage"] = "Feedback updated successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Feedback not found.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error updating feedback: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while updating feedback.";
            }

            return RedirectToAction("Detail", new { id = model.FeedbackId });
        }

        /// <summary>
        /// POST: /FeedbackManager/Link
        /// Links two feedback items together
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Link(FeedbackLinkModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Both feedback IDs are required.";
                return RedirectToAction("Index");
            }

            try
            {
                var success = await _feedbackService.LinkFeedbacksAsync(model.FeedbackId, model.LinkedFeedbackId);
                if (success)
                {
                    TempData["SuccessMessage"] = "Feedbacks linked successfully.";
                }
                else
                {
                    TempData["ErrorMessage"] = "One or both feedback items not found.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error linking feedbacks: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while linking feedbacks.";
            }

            return RedirectToAction("Detail", new { id = model.FeedbackId });
        }

        /// <summary>
        /// POST: /FeedbackManager/Unlink/{id}
        /// Removes the link from a feedback item
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Unlink(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return RedirectToAction("Index");
            }

            try
            {
                var success = await _feedbackService.UnlinkFeedbackAsync(id);
                if (success)
                {
                    TempData["SuccessMessage"] = "Feedback unlinked.";
                }
                else
                {
                    TempData["ErrorMessage"] = "Feedback not found.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError($"Error unlinking feedback: {ex.Message}");
                TempData["ErrorMessage"] = "An error occurred while unlinking feedback.";
            }

            return RedirectToAction("Detail", new { id });
        }

        /// <summary>
        /// GET: /FeedbackManager/Search?query=...
        /// AJAX endpoint for searching feedbacks to link
        /// </summary>
        public async Task<ActionResult> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                return Json(new object[0], JsonRequestBehavior.AllowGet);
            }

            try
            {
                var results = await _feedbackService.SearchFeedbacksByTitleAsync(query);
                return Json(results, JsonRequestBehavior.AllowGet);
            }
            catch
            {
                return Json(new object[0], JsonRequestBehavior.AllowGet);
            }
        }
    }
}
