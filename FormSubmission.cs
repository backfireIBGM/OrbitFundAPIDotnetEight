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
            string title,
            string description,
            string goals,
            string type,
            DateTime? launchDate,
            string teamInfo,
            IFormFileCollection? images, // Made collection nullable
            IFormFile? video,
            IFormFileCollection? documents,
            decimal fundingGoal,
            int duration,
            string budgetBreakdown,
            string? rewards
        )
        {
            // --- Initial Data Validation ---
            if (string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description) ||
                string.IsNullOrWhiteSpace(goals) ||
                string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(teamInfo) ||
                string.IsNullOrWhiteSpace(budgetBreakdown))
            {
                _logger.LogWarning("Received submission with missing required text fields.");
                return BadRequest("Please ensure all required fields (Title, Description, Goals, Type, Team Info, Budget Breakdown) are filled.");
            }

            bool fileOperationsSucceeded = true;

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

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync();

                    using (MySqlCommand command = new MySqlCommand("CALL AddMissionData(@pTitle, @pDescription, @pGoals, @pType, @pLaunchDate, @pTeamInfo, @pFundingGoal, @pDuration, @pBudgetBreakdown, @pRewards)", connection))
                    {
                        command.Parameters.AddWithValue("@pTitle", title);
                        command.Parameters.AddWithValue("@pDescription", description);
                        command.Parameters.AddWithValue("@pGoals", goals);
                        command.Parameters.AddWithValue("@pType", type);
                        command.Parameters.AddWithValue("@pLaunchDate", launchDate.HasValue ? (object)launchDate.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@pTeamInfo", teamInfo);
                        command.Parameters.AddWithValue("@pFundingGoal", fundingGoal);
                        command.Parameters.AddWithValue("@pDuration", duration);
                        command.Parameters.AddWithValue("@pBudgetBreakdown", budgetBreakdown);
                        command.Parameters.AddWithValue("@pRewards", rewards ?? (object)DBNull.Value);

                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Successfully stored core mission data for: '{title}'.");
                    }

                    // --- FILE SAVING LOGIC ---

                    // Save Mission Images
                    // Added null check for 'images' collection and 'imageFile' within loop
                    if (images != null && images.Any()) // Use .Any() for clarity, equivalent to Count > 0
                    {
                        foreach (var imageFile in images)
                        {
                            // Null check on imageFile itself, and then on FileName
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
                            }
                            else if (imageFile != null && (imageFile.FileName == null || imageFile.Length == 0)) {
                                _logger.LogWarning("Encountered an empty or null-named image file. Skipping.");
                                fileOperationsSucceeded = false; // Or choose not to mark as failure if skipping empty is OK
                            }
                        }
                    }

                    // Save Mission Video
                    // Added null check for 'video' and 'video.FileName'
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
                        fileOperationsSucceeded = false; // Or choose not to mark as failure
                    }

                    // Save Technical Documents
                    // Added null check for 'documents' collection and 'docFile' within loop
                    if (documents != null && documents.Any())
                    {
                        foreach (var docFile in documents)
                        {
                            // Null check for docFile and docFile.FileName
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
                            }
                             else if (docFile != null && (docFile.FileName == null || docFile.Length == 0)) {
                                _logger.LogWarning("Encountered an empty or null-named document file. Skipping.");
                                fileOperationsSucceeded = false; // Or choose not to mark as failure
                            }
                        }
                    }

                    if (!fileOperationsSucceeded)
                    {
                        _logger.LogWarning($"Some files were not saved successfully, but core mission data for '{title}' was saved.");
                    }
                } // End of using MySqlConnection

                return Ok($"Mission '{title}' core data submitted successfully! File upload issues may have occurred, check logs.");
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