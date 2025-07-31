using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http; // Required for IFormFile
using MySql.Data.MySqlClient;
using System; // For Path, Guid, FileStream, Exception
using System.IO; // For Path, FileStream
using System.Collections.Generic; // For List
using System.Linq; // For LINQ methods like Any()
using System.Threading.Tasks; // For Task

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController] // Indicates this is an API controller
    [Route("api/[controller]")] // Defines the base route, e.g., /api/submission
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration;

        // Define a directory to save uploads. Ensure this directory exists and has write permissions.
        // In a real app, this path would likely be configured externally (e.g., appsettings.json).
        private readonly string _uploadDirectory = Path.Combine(Directory.GetCurrentDirectory(), "uploads");

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

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
                    // Depending on your app's requirements, you might want to throw here
                    // if file uploads are critical for startup. For testing, logging is fine.
                }
            }
        }

        [HttpPost] // This method handles HTTP POST requests to /api/submission
        // The 'name' attribute in the HTML form inputs will be used for model binding.
        // For files, use IFormFile. For text, you can use string or other types.
        public async Task<IActionResult> HandleMissionSubmission(
            string? title, // Made all parameters nullable for broader testing
            string? description,
            string? goals,
            string? type,
            DateTime? launchDate,
            string? teamInfo,
            IFormFileCollection? images, // Nullable collection for multiple files
            IFormFile? video,          // Nullable for optional single file
            IFormFileCollection? documents, // Nullable collection for optional multiple files
            decimal fundingGoal,      // Using decimal for currency
            int duration,             // If not provided, will default to 0
            string? budgetBreakdown,
            string? rewards           // Nullable string for optional rewards
        )
        {
            // --- TEMPORARILY DISABLED Initial Validation for Testing ---
            // Commenting out the entire validation block to allow ANY submission to proceed.
            // This is for debugging purposes only. Re-enable and refine once the API works.
            /*
            if (
                string.IsNullOrWhiteSpace(title) &&
                string.IsNullOrWhiteSpace(description) &&
                string.IsNullOrWhiteSpace(goals) &&
                string.IsNullOrWhiteSpace(type) &&
                !launchDate.HasValue &&
                string.IsNullOrWhiteSpace(teamInfo) &&
                (images == null || !images.Any()) &&
                video == null &&
                (documents == null || !documents.Any()) &&
                fundingGoal == 0 &&
                duration == 0 &&
                string.IsNullOrWhiteSpace(budgetBreakdown)
            )
            {
                _logger.LogWarning("Received a completely empty submission payload.");
                return BadRequest("Submission payload is empty. Please provide some mission details.");
            }
            */

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
            bool fileOperationsSucceeded = true; // Flag to track if all file save operations completed without error.

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync(); // Open the connection

                    // --- Prepare and execute the primary data insertion ---
                    // !!! IMPORTANT !!!
                    // Your stored procedure 'AddMissionData' MUST be able to accept ALL these parameters.
                    // It must also be designed to handle NULL or default values (like 0 for numbers, or empty strings)
                    // for fields that might not be provided by the client.
                    using (MySqlCommand command = new MySqlCommand("CALL AddMissionData(@pTitle, @pDescription, @pGoals, @pType, @pLaunchDate, @pTeamInfo, @pFundingGoal, @pDuration, @pBudgetBreakdown, @pRewards)", connection))
                    {
                        // Add parameters, providing DBNull.Value for null/empty fields that can be null in DB
                        command.Parameters.AddWithValue("@pTitle", title ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pDescription", description ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pGoals", goals ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@pType", type ?? (object)DBNull.Value);
                        // Handle nullable date: If launchDate is null, pass DBNull.Value
                        command.Parameters.AddWithValue("@pLaunchDate", launchDate.HasValue ? (object)launchDate.Value : DBNull.Value);
                        command.Parameters.AddWithValue("@pTeamInfo", teamInfo ?? (object)DBNull.Value);
                        // Pass numbers directly; if not sent, they'll be their default (0) which your SP must handle.
                        command.Parameters.AddWithValue("@pFundingGoal", fundingGoal);
                        command.Parameters.AddWithValue("@pDuration", duration);
                        command.Parameters.AddWithValue("@pBudgetBreakdown", budgetBreakdown ?? (object)DBNull.Value);
                        // Handle nullable rewards parameter
                        command.Parameters.AddWithValue("@pRewards", rewards ?? (object)DBNull.Value);

                        await command.ExecuteNonQueryAsync(); // Execute the stored procedure
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
                } // End of using MySqlConnection

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