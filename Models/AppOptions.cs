public sealed class AppOptions
{
    public string? OpenAiBaseUrl { get; set; }
    public string? MessengerVerifyToken { get; set; }
    public string? MessengerPageAccessToken { get; set; }
    public string? MessengerAppSecret { get; set; }
    public string? MessengerGraphApiVersion { get; set; }
    public string? AdminUsername { get; set; }
    public string? AdminPassword { get; set; }
    public string? AdminToken { get; set; }
    public int? AdminSessionHours { get; set; }
    public string? SystemPrompt { get; set; }
}
