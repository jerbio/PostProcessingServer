using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using PostProcessingServer.Models;
using PostProcessingServer.Filters;
using PostProcessingServer.Services;

namespace PostProcessingServer.Controllers
{
    /// <summary>
    /// Controller for handling analysis job submissions and status queries
    /// </summary>
    [RoutePrefix("api/analysisjob")]
    public class AnalysisJobController : ApiController
    {
        private static readonly AnalysisJobQueue _jobQueue = AnalysisJobQueue.Instance;

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet]
        [Route("health")]
        [ResponseType(typeof(object))]
        public IHttpActionResult Health()
        {
            var stats = _jobQueue.GetStatistics();
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTimeOffset.UtcNow,
                queueDepth = stats.QueueDepth,
                activeJobs = stats.ActiveJobs
            });
        }

        /// <summary>
        /// Submit a suggestion analysis job
        /// </summary>
        [HttpPost]
        [Route("suggestion")]
        [ServerAuthentication]
        [ResponseType(typeof(AnalysisJobResponse))]
        public async Task<IHttpActionResult> SubmitSuggestionJob([FromBody] AnalysisJobRequest request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var job = new AnalysisJob
                {
                    TilerUserId = request.UserId,
                    JobType = AnalysisJobType.Suggestion,
                    RequestData = request.RequestData,
                    Status = AnalysisJobStatus.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _jobQueue.Enqueue(job);

                var response = AnalysisJobResponse.CreateSuccess(job.JobId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return Ok(AnalysisJobResponse.CreateError($"Failed to queue job: {ex.Message}"));
            }
        }

        /// <summary>
        /// Submit a schedule analysis job
        /// </summary>
        [HttpPost]
        [Route("analyze")]
        [ServerAuthentication]
        [ResponseType(typeof(AnalysisJobResponse))]
        public async Task<IHttpActionResult> SubmitScheduleAnalysisJob([FromBody] AnalysisJobRequest request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var job = new AnalysisJob
                {
                    TilerUserId = request.UserId,
                    JobType = AnalysisJobType.ScheduleAnalysis,
                    RequestData = request.RequestData,
                    Status = AnalysisJobStatus.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _jobQueue.Enqueue(job);

                var response = AnalysisJobResponse.CreateSuccess(job.JobId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return Ok(AnalysisJobResponse.CreateError($"Failed to queue job: {ex.Message}"));
            }
        }

        /// <summary>
        /// Submit a ProcessRequest preview job for a VibeRequest
        /// </summary>
        [HttpPost]
        [Route("processrequest")]
        [ServerAuthentication]
        [ResponseType(typeof(AnalysisJobResponse))]
        public async Task<IHttpActionResult> SubmitProcessRequestJob([FromBody] AnalysisJobRequest request)
        {
            if (request == null || !ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var job = new AnalysisJob
                {
                    TilerUserId = request.UserId,
                    JobType = AnalysisJobType.ProcessRequest,
                    RequestData = request.RequestData,
                    Status = AnalysisJobStatus.Queued,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _jobQueue.Enqueue(job);

                var response = AnalysisJobResponse.CreateSuccess(job.JobId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return Ok(AnalysisJobResponse.CreateError($"Failed to queue job: {ex.Message}"));
            }
        }

        /// <summary>
        /// Get the status of a job
        /// </summary>
        [HttpGet]
        [Route("status/{jobId}")]
        [ServerAuthentication]
        [ResponseType(typeof(AnalysisJobStatusResponse))]
        public IHttpActionResult GetJobStatus(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest("JobId is required");
            }

            var job = _jobQueue.GetJob(jobId);
            
            if (job == null)
            {
                return Ok(AnalysisJobStatusResponse.CreateNotFound(jobId));
            }

            return Ok(AnalysisJobStatusResponse.FromJob(job));
        }

        /// <summary>
        /// Get queue statistics (admin endpoint)
        /// </summary>
        [HttpGet]
        [Route("stats")]
        [ServerAuthentication]
        [ResponseType(typeof(QueueStatistics))]
        public IHttpActionResult GetStatistics()
        {
            var stats = _jobQueue.GetStatistics();
            return Ok(stats);
        }

        /// <summary>
        /// Get jobs for a specific user
        /// </summary>
        [HttpGet]
        [Route("user/{userId}")]
        [ServerAuthentication]
        [ResponseType(typeof(AnalysisJob[]))]
        public IHttpActionResult GetUserJobs(string userId, [FromUri] int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest("UserId is required");
            }

            var jobs = _jobQueue.GetUserJobs(userId, limit);
            return Ok(jobs);
        }
    }
}
