public sealed class AppOptions
{
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiModel { get; set; }
    public string? MessengerVerifyToken { get; set; }
    public string? MessengerPageAccessToken { get; set; }
    public string? MessengerAppSecret { get; set; }
    public string? MessengerGraphApiVersion { get; set; }
    public string? AdminToken { get; set; }
    public string? SystemPrompt { get; set; }
}
