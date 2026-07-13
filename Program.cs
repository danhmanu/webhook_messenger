using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.AddHttpClient<OpenAiService>();
builder.Services.AddHttpClient<MessengerService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "Messenger OpenAI Webhook",
    status = "running"
}));

app.MapGet("/webhook", (HttpRequest request, IConfiguration config) =>
{
    var mode = request.Query["hub.mode"].ToString();
    var token = request.Query["hub.verify_token"].ToString();
    var challenge = request.Query["hub.challenge"].ToString();
    var verifyToken = config["App:MessengerVerifyToken"];

    if (mode == "subscribe" && token == verifyToken)
    {
        return Results.Text(challenge);
    }

    return Results.Unauthorized();
});

app.MapPost("/webhook", async (
    MessengerWebhookPayload payload,
    OpenAiService openAi,
    MessengerService messenger,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("MessengerWebhook");

    if (payload.Object != "page")
    {
        return Results.Ok();
    }

    foreach (var entry in payload.Entry ?? [])
    {
        foreach (var messaging in entry.Messaging ?? [])
        {
            var senderId = messaging.Sender?.Id;
            var text = messaging.Message?.Text;

            if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            try
            {
                await messenger.SendTypingOnAsync(senderId, cancellationToken);
                var answer = await openAi.CreateChatReplyAsync(text, cancellationToken);
                await messenger.SendTextAsync(senderId, answer, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to handle message from sender {SenderId}", senderId);
                await messenger.SendTextAsync(
                    senderId,
                    "Xin loi, hien tai minh chua tra loi duoc. Ban thu lai sau nhe.",
                    cancellationToken);
            }
        }
    }

    return Results.Ok();
});

app.Run();

sealed class OpenAiService(HttpClient httpClient, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _apiKey = config["App:OpenAiApiKey"]
        ?? throw new InvalidOperationException("Missing App:OpenAiApiKey");

    private readonly string _model = config["App:OpenAiModel"] ?? "gpt-4o-mini";

    private readonly string _systemPrompt = config["App:SystemPrompt"]
        ?? "Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet.";

    public async Task<string> CreateChatReplyAsync(string userText, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var body = new
        {
            model = _model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "input_text", text = _systemPrompt }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "input_text", text = userText }
                    }
                }
            }
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI API failed: {(int)response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString()?.Trim() ?? "Minh chua co cau tra loi phu hop.";
        }

        return ExtractFirstText(root) ?? "Minh chua co cau tra loi phu hop.";
    }

    private static string? ExtractFirstText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text))
                {
                    return text.GetString()?.Trim();
                }
            }
        }

        return null;
    }
}

sealed class MessengerService(HttpClient httpClient, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _pageAccessToken = config["App:MessengerPageAccessToken"]
        ?? throw new InvalidOperationException("Missing App:MessengerPageAccessToken");

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
        var url = $"https://graph.facebook.com/v20.0/me/messages?access_token={Uri.EscapeDataString(_pageAccessToken)}";
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

sealed class AppOptions
{
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiModel { get; set; }
    public string? MessengerVerifyToken { get; set; }
    public string? MessengerPageAccessToken { get; set; }
    public string? SystemPrompt { get; set; }
}

sealed class MessengerWebhookPayload
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<MessengerEntry>? Entry { get; set; }
}

sealed class MessengerEntry
{
    [JsonPropertyName("messaging")]
    public List<MessengerMessaging>? Messaging { get; set; }
}

sealed class MessengerMessaging
{
    [JsonPropertyName("sender")]
    public MessengerUser? Sender { get; set; }

    [JsonPropertyName("message")]
    public MessengerMessage? Message { get; set; }
}

sealed class MessengerUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

sealed class MessengerMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
