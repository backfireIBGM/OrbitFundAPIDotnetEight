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

        private readonly string? _b2AccessKeyId;
        private readonly string? _b2ApplicationKey;
        private readonly string? _b2ServiceUrl;
        private readonly string? _b2BucketName;

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _logger.LogInformation("Attempting to load configuration for Backblaze B2 S3.");

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
        )
        {
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

            string? connectionString = _configuration.GetConnectionString("connectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set in configuration.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }
            else
            {
                _logger.LogInformation("MySQL Connection string 'connectionString' successfully loaded.");
            }


            List<string> savedImageUrls = new List<string>();
            List<string> savedVideoUrls = new List<string>();
            List<string> savedDocUrls = new List<string>();
            bool fileOperationsSucceeded = true;

            if (string.IsNullOrEmpty(_b2AccessKeyId) || string.IsNullOrEmpty(_b2ApplicationKey) ||
                string.IsNullOrEmpty(_b2ServiceUrl) || string.IsNullOrEmpty(_b2BucketName))
            {
                _logger.LogError("Cannot proceed with S3 operations: One or more Backblaze B2 S3 configuration keys are missing or null.");
                return StatusCode(500, "Server configuration error: Backblaze B2 S3 storage not properly configured.");
            }

            // >>>>>> START: S3 Client/Config Instantiation Test (Modified) <<<<<<
            try
            {
                _logger.LogInformation("Attempting to instantiate AmazonS3Config and BasicAWSCredentials.");
                var s3ConfigTest = new AmazonS3Config
                {
                    ServiceURL = _b2ServiceUrl,
                    ForcePathStyle = true,
                };
                var credentialsTest = new BasicAWSCredentials(_b2AccessKeyId, _b2ApplicationKey);
                _logger.LogInformation("AmazonS3Config and BasicAWSCredentials instantiated successfully!");
            }
            catch (Exception ex) // <<<<< This is the catch block being hit!
            {
                // >>>>> MODIFICATION HERE: Return the actual exception message to the client <<<<<
                _logger.LogError(ex, "FATAL ERROR: Failed to instantiate AmazonS3Config or BasicAWSCredentials! Exception: {Message}", ex.Message); // Keep for local logs if it ever works
                return StatusCode(500, $"Server configuration error: Failed to initialize S3 client components. Details: {ex.Message}"); // THIS LINE IS CHANGED!
            }
            // >>>>>> END: S3 Client/Config Instantiation Test (Modified) <<<<<<


            // >>>>>> Temporary return to isolate the S3 client/config test <<<<<<
            // The rest of the S3 and DB logic is temporarily commented out/not run.
            return Ok("S3 Client/Config Instantiation Test Passed! Proceeding to next step.");
        }
    }
}