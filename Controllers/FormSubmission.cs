using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration;

        // Configuration for Backblaze B2 S3-Compatible Storage
        private readonly string? _b2AccessKeyId;
        private readonly string? _b2ApplicationKey;
        private readonly string? _b2ServiceUrl;
        private readonly string? _b2BucketName;

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _logger.LogInformation("Attempting to load configuration for Backblaze B2 S3.");

            // Re-added Backblaze B2 S3 key assignments and checks (from Stage 2)
            _b2AccessKeyId = _configuration["BackblazeB2S3:AccessKeyId"];
            _b2ApplicationKey = _configuration["BackblazeB2S3:ApplicationKey"];
            _b2ServiceUrl = _configuration["BackblazeB2S3:ServiceUrl"];
            _b2BucketName = _configuration["BackblazeB2S3:BucketName"];

            if (string.IsNullOrEmpty(_b2AccessKeyId))
            {
                _logger.LogError("BackblazeB2S3:AccessKeyId is NULL or empty in configuration.");
                throw new InvalidOperationException("BackblazeB2S3:AccessKeyId not configured.");
            }
            if (string.IsNullOrEmpty(_b2ApplicationKey))
            {
                _logger.LogError("BackblazeB2S3:ApplicationKey is NULL or empty in configuration.");
                throw new InvalidOperationException("BackblazeB2S3:ApplicationKey not configured.");
            }
            if (string.IsNullOrEmpty(_b2ServiceUrl))
            {
                _logger.LogError("BackblazeB2S3:ServiceUrl is NULL or empty in configuration.");
                throw new InvalidOperationException("BackblazeB2S3:ServiceUrl not configured.");
            }
            if (string.IsNullOrEmpty(_b2BucketName))
            {
                _logger.LogError("BackblazeB2S3:BucketName is NULL or empty in configuration.");
                throw new InvalidOperationException("BackblazeB2S3:BucketName not configured.");
            }
            _logger.LogInformation("Backblaze B2 S3 configuration loading complete. All keys verified as non-empty.");
        }

        [HttpPost]
        public async Task<IActionResult> HandleMissionSubmission(
            // >>>>>> START: Re-added original [FromForm] parameters <<<<<<
            [FromForm] string? title,
            [FromForm] string? description,
            [FromForm] string? goals,
            [FromForm] string? type,
            [FromForm] DateTime? launchDate,
            [FromForm] string? teamInfo,
            [FromForm] List<IFormFile>? images,
            [FromForm] List<IFormFile>? video,
            [FromForm] List<IFormFile>? documents,
            [FromForm] decimal? fundingGoal,
            [FromForm] int? duration,
            [FromForm] string? budgetBreakdown,
            [FromForm] string? rewards
            // >>>>>> END: Re-added original [FromForm] parameters <<<<<<
        )
        {
            // >>>>>> START: Re-added initial method body logging and ReadFormAsync <<<<<<
            _logger.LogInformation("CT={ct}", Request.ContentType);

            try
            {
                var form = await Request.ReadFormAsync();
                _logger.LogInformation("Form keys: {keys}", string.Join(", ", form.Keys));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ReadFormAsync failed");
            }

            _logger.LogInformation("--- Incoming Submission Data ---");
            _logger.LogInformation($"Title: {title ?? "NULL"}");
            // ... (keep the rest of the logging for all parameters)
            _logger.LogInformation($"Description: {description ?? "NULL"}");
            _logger.LogInformation($"Goals: {goals ?? "NULL"}");
            _logger.LogInformation($"Type: {type ?? "NULL"}");
            _logger.LogInformation($"Launch Date: {launchDate?.ToString() ?? "NULL"}");
            _logger.LogInformation($"Team Info: {teamInfo ?? "NULL"}");
            _logger.LogInformation($"Funding Goal: {fundingGoal}");
            _logger.LogInformation($"Duration: {duration}");
            _logger.LogInformation($"Budget Breakdown: {budgetBreakdown ?? "NULL"}");
            _logger.LogInformation($"Rewards: {rewards ?? "NULL"}");
            _logger.LogInformation($"Image Count: {(images != null ? images.Count : 0)}");
            _logger.LogInformation($"Video Count: {(video != null ? video.Count : 0)}");
            _logger.LogInformation($"Document Count: {(documents != null ? documents.Count : 0)}");
            _logger.LogInformation("--- End Incoming Submission Data ---");
            // >>>>>> END: Re-added initial method body logging and ReadFormAsync <<<<<<

            // Returning OK for now, the rest of the logic (DB, S3) will come later
            return Ok("Submission endpoint reached (Stage 3) - Parameters bound!");
        }
    }
}