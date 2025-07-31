using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            if (!Directory.Exists(_uploadDirectory))
            {
                try
                {
                    Directory.CreateDirectory(_uploadDirectory);
                    _logger.LogInformation($"Created upload directory: {_uploadDirectory}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create upload directory: {Directory}", _uploadDirectory);
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> HandleMissionSubmission(
            string? title, string? description, string? goals, string? type,
            DateTime? launchDate, string? teamInfo,
            IFormFileCollection? images, // Nullable collection
            IFormFile? video,          // Nullable
            IFormFileCollection? documents, // Nullable collection
            decimal fundingGoal,
            int duration,
            string? budgetBreakdown,
            string? rewards
        )
        {
            // --- Relaxed Initial Validation for Testing ---
            // Removed the strict check for ALL fields being non-whitespace.
            // We'll still do minimal checks if the caller actually sent data.
            
            // A very basic check: did we get *any* data, or is the request truly empty?
            // This is a heuristic for testing; in production, you'd want stricter validation.
            if (
                string.IsNullOrWhiteSpace(title) &&
                string.IsNullOrWhiteSpace(description) &&
                string.IsNullOrWhiteSpace(goals) &&
                string.IsNullOrWhiteSpace(type) &&
                !launchDate.HasValue &&
                string.IsNullOrWhiteSpace(teamInfo) &&
                // (images == null || !images.Any()) &&
                // video == null &&
                (documents == null || !documents.Any()) &&
                fundingGoal == 0 && // Assuming default is 0 if not sent
                duration == 0 &&     // Assuming default is 0 if not sent
                string.IsNullOrWhiteSpace(budgetBreakdown)
            )
            {
                // If the request is *completely* empty, it's still likely a bad request from a functional standpoint.
                // This prevents errors if no form data at all is sent.
                _logger.LogWarning("Received a completely empty submission payload.");
                return BadRequest("Submission payload is empty. Please provide some mission details.");
            }

            // --- Database Operation (Primary Goal) ---
            string? connectionString = _configuration.GetConnectionString("connectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }

            List<string> savedImagePaths = new List<string>();
            string? savedVideoPath = null;
            List<string> savedDocPaths = new List<string>();
            bool fileOperationsSucceeded = true;

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    // --- Prepare and execute the primary data insertion ---
                    // The stored procedure MUST be able to handle NULL/empty values for all parameters.
                    using (MySqlCommand command = new MySqlCommand("CALL AddMissionData(@pTitle, @pDescription, @pGoals, @pType, @pLaunchDate, @pTeamInfo, @pFundingGoal, @pDuration, @pBudgetBreakdown, @pRewards)", connection))
                    {
                        // Add parameters, providing DBNull.Value for null/empty fields that can be null in DB
                        command.Parameters.AddWithValue("@pTitle", title ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pDescription", description ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pGoals", goals ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pType", type ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pLaunchDate", launchDate.HasValue ? (object)launchDate.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@pTeamInfo", teamInfo ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pFundingGoal", fundingGoal); // If not provided, will be 0
                        command.Parameters.AddWithValue("@pDuration", duration);     // If not provided, will be 0
                        command.Parameters.AddWithValue("@pBudgetBreakdown", budgetBreakdown ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pRewards", rewards ?? (object)DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Successfully stored core mission data (potentially empty) for: '{title ?? "N/A"}'.");
                    }

                    // --- FILE SAVING LOGIC (with null checks and individual try-catch) ---
                    // (Keep the same file saving logic as before, with the CS8602 fixes)

                    // Save Mission Images
                    if (images != null && images.Any())
                    {
                        foreach (var imageFile in images)
                        {
                            if (imageFile != null && imageFile.Length > 0 && imageFile.FileName != null)
                            {
                                try
                                {
                                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                                    string filePath = Path.Combine(_uploadDirectory, fileName);
                                    using (var stream = new FileStream(filePath, FileMode.Create))
                                    {
                                        await imageFile.CopyToAsync(stream);
                                    }
                                    savedImagePaths.Add(filePath);
                                    _logger.LogInformation($"Successfully saved image: {filePath}");
                                }
                                catch (Exception fileEx)
                                {
                                    _logger.LogError(fileEx, "Failed to save image. Submitting form data without this file. Error: {Message}", fileEx.Message);
                                    fileOperationsSucceeded = false;
                                }
                            } else {
                                _logger.LogWarning("Encountered an empty or null-named image file. Skipping.");
                                fileOperationsSucceeded = false;
                            }
                        }
                    }

                    // Save Mission Video
                    if (video != null && video.Length > 0 && video.FileName != null)
                    {
                        try
                        {
                            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(video.FileName);
                            string filePath = Path.Combine(_uploadDirectory, fileName);
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await video.CopyToAsync(stream);
                            }
                            savedVideoPath = filePath;
                            _logger.LogInformation($"Successfully saved video: {filePath}");
                        }
                        catch (Exception fileEx)
                        {
                            _logger.LogError(fileEx, "Failed to save video. Submitting form data without this file. Error: {Message}", fileEx.Message);
                            fileOperationsSucceeded = false;
                        }
                    } else if (video != null && (video.FileName == null || video.Length == 0)) {
                        _logger.LogWarning("Encountered an empty or null-named video file. Skipping.");
                        fileOperationsSucceeded = false;
                    }

                    // Save Technical Documents
                    if (documents != null && documents.Any())
                    {
                        foreach (var docFile in documents)
                        {
                            if (docFile != null && docFile.Length > 0 && docFile.FileName != null)
                            {
                                try
                                {
                                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(docFile.FileName);
                                    string filePath = Path.Combine(_uploadDirectory, fileName);
                                    using (var stream = new FileStream(filePath, FileMode.Create))
                                    {
                                        await docFile.CopyToAsync(stream);
                                    }
                                    savedDocPaths.Add(filePath);
                                    _logger.LogInformation($"Successfully saved document: {filePath}");
                                }
                                catch (Exception fileEx)
                                {
                                    _logger.LogError(fileEx, "Failed to save document. Submitting form data without this file. Error: {Message}", fileEx.Message);
                                    fileOperationsSucceeded = false;
                                }
                            } else {
                                _logger.LogWarning("Encountered an empty or null-named document file. Skipping.");
                                fileOperationsSucceeded = false;
                            }
                        }
                    }

                    if (!fileOperationsSucceeded)
                    {
                        _logger.LogWarning($"Some files were not saved successfully, but core mission data for '{title ?? "N/A"}' was saved.");
                    }
                }

                return Ok($"Mission '{title ?? "N/A"}' core data submitted successfully! File upload issues may have occurred, check logs.");
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL Error during submission: {Message}", ex.Message);
                return StatusCode(500, $"Database error processing your submission: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General Error during submission processing: {Message}", ex.Message);
                return StatusCode(500, $"An internal error occurred processing your submission: {ex.Message}");
            }
        }
    }
}