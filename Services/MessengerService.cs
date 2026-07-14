using System.Text.Json;

public sealed class MessengerService(HttpClient httpClient, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? _pageAccessToken = config["App:MessengerPageAccessToken"];
    private readonly string _graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0";

    public Task SendTypingOnAsync(string recipientId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            recipient = new { id = recipientId },
            sender_action = "typing_on"
        };

        return SendAsync(payload, cancellationToken);
    }

    public Task SendTextAsync(string recipientId, string text, CancellationToken cancellationToken)
    {
        var payload = new
        {
            recipient = new { id = recipientId },
            messaging_type = "RESPONSE",
            message = new { text = TrimForMessenger(text) }
        };

        return SendAsync(payload, cancellationToken);
    }

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        var pageAccessToken = AppConfig.Required(_pageAccessToken, "App:MessengerPageAccessToken");
        var url = $"https://graph.facebook.com/{_graphApiVersion}/me/messages?access_token={Uri.EscapeDataString(pageAccessToken)}";
        using var response = await httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Messenger API failed: {(int)response.StatusCode} {responseText}");
        }
    }

    private static string TrimForMessenger(string text)
    {
        const int messengerTextLimit = 2000;
        text = string.IsNullOrWhiteSpace(text) ? "Minh chua co cau tra loi phu hop." : text.Trim();
        return text.Length <= messengerTextLimit ? text : text[..messengerTextLimit];
    }
}
