using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// Add AWS S3 related namespaces
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime; // For BasicAWSCredentials

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration;

        // Configuration for IDrive S3-Compatible Storage
        private readonly string _idriveAccessKey;
        private readonly string _idriveSecretKey;
        private readonly string _idriveServiceUrl; // The endpoint provided by IDrive
        private readonly string _idriveBucketName;

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            // Retrieve IDrive S3 credentials from appsettings.json
            _idriveAccessKey = _configuration["IDriveS3:AccessKey"]
                               ?? throw new InvalidOperationException("IDriveS3:AccessKey not configured.");
            _idriveSecretKey = _configuration["IDriveS3:SecretKey"]
                               ?? throw new InvalidOperationException("IDriveS3:SecretKey not configured.");
            _idriveServiceUrl = _configuration["IDriveS3:ServiceUrl"]
                                ?? throw new InvalidOperationException("IDriveS3:ServiceUrl not configured.");
            _idriveBucketName = _configuration["IDriveS3:BucketName"]
                                ?? throw new InvalidOperationException("IDriveS3:BucketName not configured.");
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
            [FromForm] List<IFormFile>? video,     // <--- THIS IS THE FIX: video is now a List<IFormFile>
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
            _logger.LogInformation($"Video Count: {(video != null ? video.Count : 0)}"); // <--- Log Count for List
            _logger.LogInformation($"Document Count: {(documents != null ? documents.Count : 0)}");
            _logger.LogInformation("--- End Incoming Submission Data ---");

            string? connectionString = _configuration.GetConnectionString("connectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }

            List<string> savedImageUrls = new List<string>();
            List<string> savedVideoUrls = new List<string>(); // <--- Now a List<string> for multiple video URLs
            List<string> savedDocUrls = new List<string>();
            bool fileOperationsSucceeded = true;

            // Configure the S3 client for IDrive
            var s3Config = new AmazonS3Config
            {
                ServiceURL = _idriveServiceUrl,
                ForcePathStyle = true, // Often required for S3-compatible services
            };

            // Use BasicAWSCredentials with your IDrive keys
            var credentials = new BasicAWSCredentials(_idriveAccessKey, _idriveSecretKey);

            using (var s3Client = new AmazonS3Client(credentials, s3Config))
            {
                try
                {
                    // Function to upload a single file to S3-compatible storage
                    async Task<string?> UploadFileToS3(IFormFile file, string folder)
                    {
                        if (file == null || file.Length == 0 || string.IsNullOrEmpty(file.FileName))
                        {
                            _logger.LogWarning($"Skipping empty or null-named file in folder: {folder}");
                            return null;
                        }

                        // Use Guid for unique file name, preserve original extension
                        string fileName = $"{folder}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

                        try
                        {
                            var putRequest = new PutObjectRequest
                            {
                                BucketName = _idriveBucketName,
                                Key = fileName, // This is the path/name of the file in your bucket
                                InputStream = file.OpenReadStream(),
                                ContentType = file.ContentType
                            };

                            putRequest.CannedACL = S3CannedACL.PublicRead; // Set public read access

                            await s3Client.PutObjectAsync(putRequest);

                            string fileUrl = $"{_idriveServiceUrl}/{_idriveBucketName}/{fileName}";
                            _logger.LogInformation($"Successfully uploaded {file.FileName} to IDrive S3: {fileUrl}");
                            return fileUrl;
                        }
                        catch (Exception s3Ex)
                        {
                            _logger.LogError(s3Ex, "Failed to upload file '{FileName}' to IDrive S3. Error: {Message}", file.FileName, s3Ex.Message);
                            fileOperationsSucceeded = false;
                            return null;
                        }
                    }

                    // Upload Mission Images
                    if (images != null && images.Any())
                    {
                        foreach (var imageFile in images)
                        {
                            string? url = await UploadFileToS3(imageFile, "images");
                            if (url != null)
                            {
                                savedImageUrls.Add(url);
                            }
                        }
                    }

                    // Upload Mission Videos <--- FIXED LOGIC FOR LIST<IFORMFILE>
                    if (video != null && video.Any())
                    {
                        foreach (var videoFile in video) // Iterate through each video file in the list
                        {
                            string? url = await UploadFileToS3(videoFile, "videos");
                            if (url != null)
                            {
                                savedVideoUrls.Add(url);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogInformation("No video files were provided.");
                    }

                    // Upload Technical Documents
                    if (documents != null && documents.Any())
                    {
                        foreach (var docFile in documents)
                        {
                            string? url = await UploadFileToS3(docFile, "documents");
                            if (url != null)
                            {
                                savedDocUrls.Add(url);
                            }
                        }
                    }

                    // Convert lists of URLs to a single string for storage
                    string? imageUrlsString = savedImageUrls.Any() ? string.Join(",", savedImageUrls) : null;
                    string? videoUrlsString = savedVideoUrls.Any() ? string.Join(",", savedVideoUrls) : null;
                    string? docUrlsString = savedDocUrls.Any() ? string.Join(",", savedDocUrls) : null;


                    using (MySqlConnection connection = new MySqlConnection(connectionString))
                    {
                        await connection.OpenAsync();

                        string sqlString = @"
                            INSERT INTO FormSubmissions(
                                title, description, goals, type, launchDate, teamInfo, fundingGoal, duration,
                                budgetBreakdown, rewards, image_urls, video_urls, document_urls -- <--- DB column name changed for consistency
                            ) VALUES (
                                @p_title, @p_description, @p_goals, @p_type, @p_launchDate, @p_teamInfo,
                                @p_fundingGoal, @p_duration, @p_budgetBreakdown, @p_rewards,
                                @p_imageUrls, @p_videoUrls, @p_documentUrls -- <--- Parameter for video URLs list
                            )";

                        using (MySqlCommand command = new MySqlCommand(sqlString, connection))
                        {
                            command.Parameters.AddWithValue("@p_title", title ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_description", description ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_goals", goals ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_type", type ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_launchDate", launchDate ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_teamInfo", teamInfo ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_fundingGoal", fundingGoal ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_duration", duration ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_budgetBreakdown", budgetBreakdown ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@p_rewards", rewards ?? (object)DBNull.Value);

                            command.Parameters.AddWithValue("@p_imageUrls", imageUrlsString);
                            command.Parameters.AddWithValue("@p_videoUrls", videoUrlsString); // <--- Correct parameter for multiple video URLs
                            command.Parameters.AddWithValue("@p_documentUrls", docUrlsString);

                            await command.ExecuteNonQueryAsync();
                            _logger.LogInformation($"Successfully stored core mission data and IDrive S3 URLs for: '{title ?? "N/A"}'.");
                        }
                    }
                }
                catch (MySqlException ex)
                {
                    _logger.LogError(ex, "MySQL Error during submission: {Message}", ex.Message);
                    return StatusCode(500, $"Database error processing your submission: {ex.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "General Error during submission processing (incl. S3 issues): {Message}", ex.Message);
                    return StatusCode(500, $"An internal error occurred processing your submission: {ex.Message}");
                }
            }

            if (!fileOperationsSucceeded)
            {
                return Ok($"Mission '{title ?? "N/A"}' data submitted successfully! WARNING: Some files were not uploaded to IDrive S3 due to errors.");
            }

            return Ok($"Mission '{title ?? "N/A"}' and all associated files uploaded to IDrive S3 and data submitted successfully!");
        }
    }
}