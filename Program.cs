using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration; // Required for IConfiguration

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// If you want to use the minimal API endpoint explorer for tool-generated OpenAPI specs, keep this.
// Otherwise, remove it.
builder.Services.AddEndpointsApiExplorer();
// No explicit AddSwaggerGen() or AddSwaggerUI() calls.

// Add logging services. Still crucial for diagnostics.
builder.Services.AddLogging();

// CORS Configuration - keep if your frontend is on a different origin.
// Remove this entire section if your frontend and backend are served from the same origin,
// or if you don't have a frontend making cross-origin requests.
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        name: "AllowLocalDev",
        policyBuilder =>
        {
            policyBuilder
                .WithOrigins(
                    "http://127.0.0.1:5501",
                    "http://localhost:5501"
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

// No explicit UseAuthorization() if you're not implementing auth.
// app.UseAuthorization(); // Uncomment and configure if you need authorization.

// Maps controller actions.
app.MapControllers();

app.Run();