using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Microsoft.AspNetCore.Authorization;


namespace OrbitFundAPIDotnetEight.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class ApprovalController : ControllerBase
    {
        private readonly ILogger<ApprovalController> _logger;
        private readonly IConfiguration _configuration;

        public ApprovalController(ILogger<ApprovalController> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        public class SubmissionDetailsDto
        {
            public int Id { get; set; }
            public string? Title { get; set; }
            public string? Description { get; set; }
            public string? Goals { get; set; }
            public string? Type { get; set; }
            public DateTime? LaunchDate { get; set; }
            public string? TeamInfo { get; set; }
            public decimal? FundingGoal { get; set; }
            public int? Duration { get; set; }
            public string? BudgetBreakdown { get; set; }
            public string? Rewards { get; set; }
            public List<string>? ImageUrls { get; set; }
            public List<string>? VideoUrls { get; set; }
            public List<string>? DocumentUrls { get; set; }
            public string? Status { get; set; }
        }

        [HttpGet("pending-ids")]
        public async Task<IActionResult> GetPendingSubmissionIds()
        {
            string? connectionString = _configuration.GetConnectionString("connectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }

            List<int> submissionIds = new List<int>();

            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    string sql = "SELECT id FROM FormSubmissions WHERE Status = 'Pending' ORDER BY id DESC";
                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                submissionIds.Add(reader.GetInt32(reader.GetOrdinal("id")));
                            }
                        }
                    }
                    _logger.LogInformation($"Retrieved {submissionIds.Count} pending submission IDs for admin user.");
                    return Ok(submissionIds);
                }
                catch (MySqlException ex)
                {
                    _logger.LogError(ex, "MySQL Error fetching pending submission IDs: {Message}", ex.Message);
                    return StatusCode(500, $"Database error: {ex.Message}");
                }
            }
        }

        // GET: api/Approval/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetSubmissionDetails(int id)
        {
            string? connectionString = _configuration.GetConnectionString("connectionString");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("MySQL Connection string 'connectionString' is not set.");
                return StatusCode(500, "Server configuration error: Database connection string is missing.");
            }

            SubmissionDetailsDto? submission = null;

            using (MySqlConnection connection = new MySqlConnection(connectionString))
            {
                try
                {
                    await connection.OpenAsync();
                    string sql = @"
                        SELECT
                            id, title, description, goals, type, launchDate, teamInfo, fundingGoal, duration,
                            budgetBreakdown, rewards, image_urls, video_urls, document_urls, Status
                        FROM FormSubmissions
                        WHERE id = @p_id";

                    using (MySqlCommand command = new MySqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@p_id", id);
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            if (await reader.ReadAsync())
                            {
                                submission = new SubmissionDetailsDto
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    Title = reader.IsDBNull(reader.GetOrdinal("title")) ? null : reader.GetString(reader.GetOrdinal("title")),
                                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                                    Goals = reader.IsDBNull(reader.GetOrdinal("goals")) ? null : reader.GetString(reader.GetOrdinal("goals")),
                                    Type = reader.IsDBNull(reader.GetOrdinal("type")) ? null : reader.GetString(reader.GetOrdinal("type")),
                                    LaunchDate = reader.IsDBNull(reader.GetOrdinal("launchDate")) ? (DateTime?)null : reader.GetDateTime(reader.GetOrdinal("launchDate")),
                                    TeamInfo = reader.IsDBNull(reader.GetOrdinal("teamInfo")) ? null : reader.GetString(reader.GetOrdinal("teamInfo")),
                                    FundingGoal = reader.IsDBNull(reader.GetOrdinal("fundingGoal")) ? (decimal?)null : reader.GetDecimal(reader.GetOrdinal("fundingGoal")),
                                    Duration = reader.IsDBNull(reader.GetOrdinal("duration")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("duration")),
                                    BudgetBreakdown = reader.IsDBNull(reader.GetOrdinal("budgetBreakdown")) ? null : reader.GetString(reader.GetOrdinal("budgetBreakdown")),
                                    Rewards = reader.IsDBNull(reader.GetOrdinal("rewards")) ? null : reader.GetString(reader.GetOrdinal("rewards")),
                                    Status = reader.IsDBNull(reader.GetOrdinal("Status")) ? null : reader.GetString(reader.GetOrdinal("Status"))
                                };

                                submission.ImageUrls = reader.IsDBNull(reader.GetOrdinal("image_urls")) ? new List<string>() : reader.GetString(reader.GetOrdinal("image_urls")).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                                submission.VideoUrls = reader.IsDBNull(reader.GetOrdinal("video_urls")) ? new List<string>() : reader.GetString(reader.GetOrdinal("video_urls")).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                                submission.DocumentUrls = reader.IsDBNull(reader.GetOrdinal("document_urls")) ? new List<string>() : reader.GetString(reader.GetOrdinal("document_urls")).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                            }
                        }
                    }

                    if (submission == null)
                    {
                        _logger.LogWarning($"Submission with ID {id} not found for admin user.");
                        return NotFound($"Submission with ID {id} not found.");
                    }

                    _logger.LogInformation($"Retrieved details for submission ID {id} for admin user.");
                    return Ok(submission);
                }
                catch (MySqlException ex)
                {
                    _logger.LogError(ex, "MySQL Error fetching submission details for ID {Id}: {Message}", id, ex.Message);
                    return StatusCode(500, $"Database error: {ex.Message}");
                }
            }
        }
    }
}