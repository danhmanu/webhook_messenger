using Microsoft.AspNetCore.Mvc;

[ApiController]
public sealed class HealthController(IConfiguration config) : ControllerBase
{
    [HttpGet("health")]
    public IActionResult PublicHealth() => Ok(new
    {
        status = "ok",
        messengerVerifyTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerVerifyToken"]),
        messengerPageAccessTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerPageAccessToken"]),
        messengerAppSecretConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerAppSecret"]),
        chatApiBaseUrl = config["App:OpenAiBaseUrl"] ?? "http://chatbot.bvdkgiadinh.com:5000/api",
        graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0"
    });

    [HttpGet("api/v1/health")]
    public IActionResult ApiHealth() => Ok(new
    {
        status = "ok",
        messengerVerifyTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerVerifyToken"]),
        messengerPageAccessTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerPageAccessToken"]),
        messengerAppSecretConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerAppSecret"]),
        adminTokenConfigured = !string.IsNullOrWhiteSpace(config["App:AdminToken"]),
        chatApiBaseUrl = config["App:OpenAiBaseUrl"] ?? "http://chatbot.bvdkgiadinh.com:5000/api",
        graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0"
    });
}
