using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OrbitFundAPIDotnetEight.Models;

namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController] // Indicates this is an API controller
    [Route("api/[controller]")] // Defines the base route, e.g., /api/submission
    public class SubmissionController : ControllerBase
    {
        private readonly ILogger<SubmissionController> _logger;
        private readonly IConfiguration _configuration; // To read connection string

        public SubmissionController(ILogger<SubmissionController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost] // This method handles HTTP POST requests to /api/submission
        public async Task<IActionResult> HandleMissionSubmission([FromBody] SubmissionPayload data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Name))
            {
                _logger.LogWarning("Received submission with missing or empty 'Name'.");
                return BadRequest("Name field is required in the JSON payload.");
            }

            // Get the connection string from appsettings.json (or env vars/Azure config)
            string? connectionString = _configuration.GetConnectionString("connectionString");

            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }

            try
            {
                using (MySqlConnection connection = new MySqlConnection(connectionString))
                {
                    await connection.OpenAsync(); // Open the connection

                    using (MySqlCommand command = new MySqlCommand("CALL AddFormSubmission(@pName)", connection)) // CALL your stored procedure
                    {
                        // Add parameters
                        command.Parameters.AddWithValue("@pName", data.Name);

                        await command.ExecuteNonQueryAsync(); // Execute the stored procedure
                    }
                }

                _logger.LogInformation($"Successfully stored '{data.Name}' in the database via stored procedure!");
                return Ok($"Successfully stored '{data.Name}' in the database via stored procedure!");
            }
            catch (MySqlException ex)
            {
                _logger.LogError(ex, "MySQL Error: {Message}", ex.Message);
                return StatusCode(500, $"Database error processing your submission: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "General Error: {Message}", ex.Message);
                return StatusCode(500, $"Error processing your submission: {ex.Message}");
            }
        }
    }
}