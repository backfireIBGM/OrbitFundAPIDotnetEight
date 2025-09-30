using MySql.Data.MySqlClient; // For MySqlConnection
using System.Data; // For IDbConnection
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Add services to the container.
builder.Services.AddControllers();

// If you want to use the minimal API endpoint explorer for tool-generated OpenAPI specs, keep this.
// Otherwise, remove it.
builder.Services.AddEndpointsApiExplorer();
// No explicit AddSwaggerGen() or AddSwaggerUI() calls.

// >>> ADD THIS: replace the bare AddLogging() <<<
builder.Logging.ClearProviders();
builder.Logging.AddConsole();                 // shows in Azure Log Stream
builder.Logging.AddDebug();                   // helpful locally

// // Optional: rolling files under /home/LogFiles/Application
// // dotnet add package Microsoft.Extensions.Logging.AzureAppServices
// builder.Logging.AddAzureWebAppDiagnostics();
// // (You can omit the package if you don't want files. Console is enough for Log Stream.)

// Add MySQL Database Connection
builder.Services.AddTransient<IDbConnection>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("connectionString");
    return new MySqlConnection(connectionString);
});

// Temporarily comment out this entire block to isolate JWT
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    builder.Configuration["JwtSettings:Key"]
                        ?? throw new InvalidOperationException("JWT Key not configured!")
                )
            )
        };
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy(
        name: "AllowLocalDev",
        policyBuilder =>
        {
            policyBuilder
                .WithOrigins(
                    "http://127.0.0.1:5501",
                    "http://localhost:5501",
                    "http://127.0.0.1:5500",
                    "https://realorbitfundapp-aeh3hnbcf8dzf4dh.westus-01.azurewebsites.net"
                )
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        }
    );
});

var app = builder.Build();

// Configure the HTTP request pipeline.

// In a development environment. No Swagger UI.
if (app.Environment.IsDevelopment())
{
    // Optionally use developer exception page for detailed error info in dev.
    app.UseDeveloperExceptionPage();
}

// Enable HTTPS redirection for security (highly recommended for production).
// app.UseHttpsRedirection(); // Uncomment if you want to enforce HTTPS

// Apply the defined CORS policy.
app.UseCors("AllowLocalDev");

// Enable routing.
app.UseRouting();

// Add Authentication and Authorization middleware
app.UseAuthentication(); // Must be before UseAuthorization
app.UseAuthorization();

// Maps controller actions.
app.MapControllers();

app.Run();