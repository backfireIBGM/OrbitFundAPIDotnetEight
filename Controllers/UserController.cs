using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OrbitFund.Models;
using System.Data;
using Dapper;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;

namespace OrbitFund.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly IDbConnection _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IDbConnection db, IConfiguration configuration, ILogger<UsersController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // Check if user already exists
            var existingUser = await _db.QueryFirstOrDefaultAsync<User>(
                "SELECT Id FROM Users WHERE Email = @Email OR Username = @Username",
                new { request.Email, request.Username }
            );

            if (existingUser != null)
            {
                return Conflict("User with this email or username already exists.");
            }

            // Hash password
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // Insert new user
            var sql = @"
                INSERT INTO Users (Username, Email, PasswordHash, CreatedAt)
                VALUES (@Username, @Email, @PasswordHash, NOW());
                SELECT LAST_INSERT_ID();";

            var userId = await _db.ExecuteScalarAsync<int>(
                sql,
                new { request.Username, request.Email, PasswordHash = passwordHash }
            );

            _logger.LogInformation("User registered: {Email}", request.Email);
            return StatusCode(201, new { Message = "User registered successfully!", UserId = userId });
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Database error during registration for email: {Email}", request.Email);
            return StatusCode(500, "An error occurred during registration. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration for email: {Email}", request.Email);
            return StatusCode(500, "An unexpected error occurred. Please try again later.");
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            // Find user by email
            var user = await _db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Email = @Email",
                new { request.Email }
            );

            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid email or password.");
            }

            var token = GenerateJwtToken(user);
            _logger.LogInformation("User logged in: {Email}", request.Email);

            return Ok(
                new LoginResponse
                {
                    Token = token,
                    Username = user.Username,
                    Message = "Login successful!"
                }
            );
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "Database error during login for email: {Email}", request.Email);
            return StatusCode(500, "An error occurred during login. Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for email: {Email}", request.Email);
            return StatusCode(500, "An unexpected error occurred. Please try again later.");
        }
    }
    [HttpGet("verifyAdmin")]
    [Authorize]
    public async Task<IActionResult> VerifyAdmin()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim))
        {
            _logger.LogWarning("VerifyAdmin: User ID claim not found in token for an authenticated user.");
            return Unauthorized("User ID not found in token.");
        }

        if (!int.TryParse(userIdClaim, out int userId))
        {
            _logger.LogError("VerifyAdmin: Failed to parse User ID claim '{UserIdClaim}' to an integer.", userIdClaim);
            return Unauthorized("Invalid User ID format in token.");
        }

        try
        {
            var sql = "SELECT AdminGrantedAt FROM Users WHERE Id = @UserId";
            var adminGrantedAt = await _db.QueryFirstOrDefaultAsync<DateTime?>(sql, new { UserId = userId });

            if (adminGrantedAt.HasValue)
            {
                _logger.LogInformation("VerifyAdmin: User {UserId} is an admin (AdminGrantedAt: {AdminGrantedAt}).", userId, adminGrantedAt.Value);
                return Ok(new { IsAdmin = true, GrantedAt = adminGrantedAt.Value });
            }
            else
            {
                _logger.LogInformation("VerifyAdmin: User {UserId} is NOT an admin.", userId);
                return Forbid("User does not have administrative privileges.");
            }
        }
        catch (MySqlException ex)
        {
            _logger.LogError(ex, "VerifyAdmin: Database error while checking admin status for user {UserId}.", userId);
            return StatusCode(500, "A database error occurred while verifying privileges.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VerifyAdmin: An unexpected error occurred while checking admin status for user {UserId}.", userId);
            return StatusCode(500, "An unexpected error occurred.");
        }
    }

    private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var jwtKey = jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key not configured in appsettings.json");
        var key = Encoding.ASCII.GetBytes(jwtKey);

        bool isAdmin = user.AdminGrantedAt.HasValue;

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email)
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin")); // Add the "Admin" role claim
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims), // Use the list of claims
            Expires = DateTime.UtcNow.AddHours(2), // Token valid for 2 hours
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            ),
            Issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer not configured in appsettings.json"),
            Audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience not configured in appsettings.json")
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}