using Microsoft.AspNetCore.Mvc;

namespace Saas.Controllers;

[ApiController]
[Route("/")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new { status = "Healthy" });
    }
}