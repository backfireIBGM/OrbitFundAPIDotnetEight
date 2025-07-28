using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging; // Don't forget this for logging!

namespace OrbitFundAPIDotnetEight.Controllers // Or whatever namespace you prefer
{
    [ApiController] // Essential for API controllers
    [Route("api/[controller]")] // This will make your route /api/test
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger; // Logger for your debugging pleasure

        public TestController(ILogger<TestController> logger)
        {
            _logger = logger;
        }

        [HttpGet("alive")] // This specific action will be at /api/test/alive
        public IActionResult Alive()
        {
            _logger.LogInformation("TestController: 'alive' endpoint hit. The API breathes!");
            return Ok("The API breathes! All systems nominal.");
        }

        [HttpGet("ping")] // Another one, just because we can. /api/test/ping
        public IActionResult Ping()
        {
            _logger.LogInformation("TestController: 'ping' endpoint hit. Pong!");
            return Ok("Pong!");
        }
    }
}