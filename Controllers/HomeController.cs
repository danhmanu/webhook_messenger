using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    [HttpGet("")]
    public IActionResult Index() => Ok(new
    {
        name = "Messenger OpenAI Webhook",
        status = "running"
    });

    [HttpGet("admin")]
    [HttpGet("admin/{*path}")]
    public IActionResult Admin() => Ok(new
    {
        message = "Admin frontend runs as a separate service.",
        url = "https://admin.vietnamhospital.cloud",
        api = "/api/v1"
    });
}
