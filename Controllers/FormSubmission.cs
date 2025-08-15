using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http; // Required for IFormFile
using MySql.Data.MySqlClient;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration; // Make sure this is explicitly included
using Microsoft.AspNetCore.Authorization; // Add this if you intend to use [Authorize]

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // Correctly resolves to /api/Submission
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _uploadDirectory; // Initialize in constructor, not at declaration

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "uploads");


            // Ensure the upload directory exists on startup.
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
                    // In a real app, this might throw an exception on startup if critical
                    // For now, logging might be enough for debugging.
                }
            }
        }

        [HttpPost] // Handles POST requests to /api/Submission
        [Authorize] // <--- ADD THIS IF YOU WANT TO PROTECT IT
        public async Task<IActionResult> HandleMissionSubmission(
            [FromForm] string? title,
            [FromForm] string? description,
            [FromForm] string? goals,
            [FromForm] string? type,
            [FromForm] DateTime? launchDate,
            [FromForm] string? teamInfo,
            [FromForm] List<IFormFile>? images,
            [FromForm] IFormFile? video,
            [FromForm] List<IFormFile>? documents,
            [FromForm] decimal? fundingGoal,
            [FromForm] int? duration,
            [FromForm] string? budgetBreakdown,
            [FromForm] string? rewards
            // Consider adding bool for termsAgree/accuracyConfirm if you need them directly in controller,
            // otherwise they're handled client-side.
            // [FromForm] bool? termsAgree,
            // [FromForm] bool? accuracyConfirm
        )
        {
            // REMOVE THIS BLOCK - It interferes with [FromForm] binding
            /*
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
            */

            // --- Model Validation (Automatically handled by [ApiController] but good to explicitly check/return) ---
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Mission submission model validation failed.");
                return BadRequest(ModelState); // This returns a ProblemDetails object with validation errors.
            }

            _logger.LogInformation("--- Incoming Submission Data ---");
            _logger.LogInformation($"Title: {title ?? "NULL"}");
            _logger.LogInformation($"Description: {description ?? "NULL"}");
            _logger.LogInformation($"Goals: {goals ?? "NULL"}");
            _logger.LogInformation($"Type: {type ?? "NULL"}");
            _logger.LogInformation($"Launch Date: {launchDate?.ToString() ?? "NULL"}");
            _logger.LogInformation($"Team Info: {teamInfo ?? "NULL"}");
            _logger.LogInformation($"Funding Goal: {fundingGoal?.ToString() ?? "NULL"}"); // Use ?.ToString() for nullable numbers
            _logger.LogInformation($"Duration: {duration?.ToString() ?? "NULL"}");       // Use ?.ToString() for nullable numbers
            _logger.LogInformation($"Budget Breakdown: {budgetBreakdown ?? "NULL"}");
            _logger.LogInformation($"Rewards: {rewards ?? "NULL"}");
            _logger.LogInformation($"Image Count: {(images != null ? images.Count : 0)}");
            _logger.LogInformation($"Video Present: {(video != null ? "Yes" : "No")}");
            _logger.LogInformation($"Document Count: {(documents != null ? documents.Count : 0)}");
            _logger.LogInformation("--- End Incoming Submission Data ---");

            // Fix the connection string name here as discussed previously
            string? connectionString = _configuration.GetConnectionString("DefaultConnection"); // <-- CHANGED THIS!
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'DefaultConnection' is not set.");
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

                    string sqlString = "INSERT INTO FormSubmissions(title, description, goals, type, launchDate, teamInfo, fundingGoal, duration, budgetBreakdown, rewards) VALUES (@p_title, @p_description, @p_goals, @p_type, @p_launchDate, @p_teamInfo, @p_fundingGoal, @p_duration, @p_budgetBreakdown, @p_rewards)";
                    using (MySqlCommand command = new MySqlCommand(sqlString, connection))
                    {
                        // Ensure parameters are handled correctly for potential nulls
                        command.Parameters.AddWithValue("@p_title", (object)title ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_description", (object)description ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_goals", (object)goals ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_type", (object)type ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_launchDate", (object)launchDate ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_teamInfo", (object)teamInfo ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_fundingGoal", (object)fundingGoal ?? DBNull.Value); // Nullable decimal/int should map correctly
                        command.Parameters.AddWithValue("@p_duration", (object)duration ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_budgetBreakdown", (object)budgetBreakdown ?? DBNull.Value);
                        command.Parameters.AddWithValue("@p_rewards", (object)rewards ?? DBNull.Value);


                        await command.ExecuteNonQueryAsync();
                        _logger.LogInformation($"Successfully stored core mission data for: '{title ?? "N/A"}'.");
                    }

                    // --- FILE SAVING LOGIC (consider moving this to a service or making more robust) ---
                    // This local file saving WILL NOT persist on Azure App Service restarts.
                    // For persistent storage, use Azure Blob Storage.

                    // Save Mission Images
                    if (images != null && images.Any())
                    {
                        foreach (var imageFile in images)
                        {
                            if (imageFile != null && imageFile.Length > 0) // No need for FileName != null, it's typically set by browser.
                            {
                                try
                                {
                                    // Ensure Path.GetExtension doesn't throw if FileName is empty/malformed
                                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName ?? ".tmp");
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
                                    _logger.LogError(fileEx, "Failed to save image. Error: {Message}", fileEx.Message);
                                    fileOperationsSucceeded = false;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Encountered an empty or null image file. Skipping.");
                                fileOperationsSucceeded = false;
                            }
                        }
                    }

                    // Save Mission Video
                    if (video != null && video.Length > 0)
                    {
                        try
                        {
                            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(video.FileName ?? ".tmp");
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
                            _logger.LogError(fileEx, "Failed to save video. Error: {Message}", fileEx.Message);
                            fileOperationsSucceeded = false;
                        }
                    }

                    // Save Technical Documents
                    if (documents != null && documents.Any())
                    {
                        foreach (var docFile in documents)
                        {
                            if (docFile != null && docFile.Length > 0)
                            {
                                try
                                {
                                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(docFile.FileName ?? ".tmp");
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
                                    _logger.LogError(fileEx, "Failed to save document. Error: {Message}", fileEx.Message);
                                    fileOperationsSucceeded = false;
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Encountered an empty or null document file. Skipping.");
                                fileOperationsSucceeded = false;
                            }
                        }
                    }

                    if (!fileOperationsSucceeded)
                    {
                        _logger.LogWarning($"Some files were not saved successfully for mission '{title ?? "N/A"}'.");
                    }
                } // End of using MySqlConnection

                return Ok(new { Message = $"Mission '{title ?? "N/A"}' submitted successfully!", FilesSaved = fileOperationsSucceeded, ImagePaths = savedImagePaths, VideoPath = savedVideoPath, DocumentPaths = savedDocPaths });
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