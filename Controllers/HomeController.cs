using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("")]
public sealed class HomeController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public HomeController(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    [HttpGet("")]
    public IActionResult Index() => Ok(new
    {
        name = "Messenger OpenAI Webhook",
        status = "running"
    });

    [HttpGet("admin")]
    [HttpGet("admin/{*path}")]
    public IActionResult Admin()
    {
        var path = Path.Combine(_environment.WebRootPath ?? "wwwroot", "index.html");
        return PhysicalFile(path, "text/html; charset=utf-8");
    }
}
