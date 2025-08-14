using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using OrbitFund.Models;
using System.Data;
using Dapper; // Used for easy data mapping
using BCrypt.Net; // For password hashing
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration; // To access JwtSettings

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

            // Generate JWT Token
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

private string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        // FIX: Ensure 'Key' is not null. Use a null-coalescing operator or throw.
        var jwtKey = jwtSettings["Key"] ?? throw new InvalidOperationException("JWT Key not configured in appsettings.json");
        var key = Encoding.ASCII.GetBytes(jwtKey);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email)
                    // Add other claims as needed (e.g., roles)
                }
            ),
            Expires = DateTime.UtcNow.AddHours(2), // Token valid for 2 hours
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature
            ),
            // Ensure Issuer and Audience are also not null
            Issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer not configured in appsettings.json"),
            Audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience not configured in appsettings.json")
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}