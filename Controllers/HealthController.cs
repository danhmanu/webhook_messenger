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
        openAiApiKeyConfigured = !string.IsNullOrWhiteSpace(config["App:OpenAiApiKey"]),
        graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0",
        openAiModel = config["App:OpenAiModel"] ?? "gpt-4o-mini"
    });

    [HttpGet("api/v1/health")]
    public IActionResult ApiHealth() => Ok(new
    {
        status = "ok",
        messengerVerifyTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerVerifyToken"]),
        messengerPageAccessTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerPageAccessToken"]),
        messengerAppSecretConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerAppSecret"]),
        adminTokenConfigured = !string.IsNullOrWhiteSpace(config["App:AdminToken"]),
        openAiApiKeyConfigured = !string.IsNullOrWhiteSpace(config["App:OpenAiApiKey"]),
        graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0",
        openAiModel = config["App:OpenAiModel"] ?? "gpt-4o-mini"
    });
}
