using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection("App"));
builder.Services.AddHttpClient<OpenAiService>();
builder.Services.AddHttpClient<MessengerService>();
builder.Services.AddSingleton<MessengerWebhookVerifier>();
builder.Services.AddSingleton<MessageSnippetStore>();

var app = builder.Build();

app.UseStaticFiles();

app.MapGet("/", () => Results.Ok(new
{
    name = "Messenger OpenAI Webhook",
    status = "running"
}));

app.MapGet("/health", (IConfiguration config) => Results.Ok(new
{
    status = "ok",
    messengerVerifyTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerVerifyToken"]),
    messengerPageAccessTokenConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerPageAccessToken"]),
    messengerAppSecretConfigured = !string.IsNullOrWhiteSpace(config["App:MessengerAppSecret"]),
    openAiApiKeyConfigured = !string.IsNullOrWhiteSpace(config["App:OpenAiApiKey"]),
    graphApiVersion = config["App:MessengerGraphApiVersion"] ?? "v25.0",
    openAiModel = config["App:OpenAiModel"] ?? "gpt-4o-mini"
}));

app.MapGet("/admin", (IWebHostEnvironment environment) =>
{
    var path = Path.Combine(environment.WebRootPath ?? "wwwroot", "admin.html");
    return Results.File(path, "text/html; charset=utf-8");
});

app.MapGet("/api/message-snippets", async (
    HttpRequest request,
    MessageSnippetStore store,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    if (!AdminAuth.IsAuthorized(request, config))
    {
        return Results.Unauthorized();
    }

    var snippets = await store.GetAllAsync(cancellationToken);
    return Results.Ok(snippets.OrderByDescending(snippet => snippet.UpdatedAt));
});

app.MapPost("/api/message-snippets", async (
    HttpRequest request,
    MessageSnippetUpsert input,
    MessageSnippetStore store,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    if (!AdminAuth.IsAuthorized(request, config))
    {
        return Results.Unauthorized();
    }

    var result = await store.CreateAsync(input, cancellationToken);
    return result is null ? Results.BadRequest() : Results.Created($"/api/message-snippets/{result.Id}", result);
});

app.MapPut("/api/message-snippets/{id}", async (
    string id,
    HttpRequest request,
    MessageSnippetUpsert input,
    MessageSnippetStore store,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    if (!AdminAuth.IsAuthorized(request, config))
    {
        return Results.Unauthorized();
    }

    var result = await store.UpdateAsync(id, input, cancellationToken);
    return result is null ? Results.NotFound() : Results.Ok(result);
});

app.MapDelete("/api/message-snippets/{id}", async (
    string id,
    HttpRequest request,
    MessageSnippetStore store,
    IConfiguration config,
    CancellationToken cancellationToken) =>
{
    if (!AdminAuth.IsAuthorized(request, config))
    {
        return Results.Unauthorized();
    }

    return await store.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
});

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
    HttpRequest request,
    MessengerWebhookVerifier verifier,
    OpenAiService openAi,
    MessengerService messenger,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    var logger = loggerFactory.CreateLogger("MessengerWebhook");

    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    var body = await reader.ReadToEndAsync(cancellationToken);

    if (!verifier.IsValid(request, body))
    {
        logger.LogWarning("Rejected webhook because X-Hub-Signature-256 is invalid or missing");
        return Results.Unauthorized();
    }

    MessengerWebhookPayload? payload;

    try
    {
        payload = JsonSerializer.Deserialize<MessengerWebhookPayload>(body, MessengerWebhookPayload.JsonOptions);
    }
    catch (JsonException ex)
    {
        logger.LogWarning(ex, "Rejected webhook because payload JSON is invalid");
        return Results.BadRequest();
    }

    if (payload?.Object != "page")
    {
        return Results.Ok();
    }

    foreach (var entry in payload.Entry ?? [])
    {
        foreach (var messaging in entry.Messaging ?? [])
        {
            var senderId = messaging.Sender?.Id;
            var text = messaging.GetUserInput();

            if (messaging.IsIgnorable || string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(text))
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

                try
                {
                    await messenger.SendTextAsync(
                        senderId,
                        "Xin loi, hien tai minh chua tra loi duoc. Ban thu lai sau nhe.",
                        cancellationToken);
                }
                catch (Exception sendError)
                {
                    logger.LogError(sendError, "Failed to send fallback message to sender {SenderId}", senderId);
                }
            }
        }
    }

    return Results.Ok();
});

app.Run();

sealed class MessengerWebhookVerifier(IConfiguration config)
{
    private readonly string? _appSecret = config["App:MessengerAppSecret"];

    public bool IsValid(HttpRequest request, string body)
    {
        if (string.IsNullOrWhiteSpace(_appSecret))
        {
            return true;
        }

        var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();

        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_appSecret),
            Encoding.UTF8.GetBytes(body));

        var expectedSignature = $"sha256={Convert.ToHexString(expectedBytes).ToLowerInvariant()}";
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
    }
}

sealed class OpenAiService(HttpClient httpClient, IConfiguration config, MessageSnippetStore snippetStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? _apiKey = config["App:OpenAiApiKey"];

    private readonly string _model = config["App:OpenAiModel"] ?? "gpt-4o-mini";

    private readonly string _systemPrompt = config["App:SystemPrompt"]
        ?? "Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet.";

    public async Task<string> CreateChatReplyAsync(string userText, CancellationToken cancellationToken)
    {
        var apiKey = AppConfig.Required(_apiKey, "App:OpenAiApiKey");

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var systemPrompt = await BuildSystemPromptAsync(cancellationToken);

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
                        new { type = "input_text", text = systemPrompt }
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

    private async Task<string> BuildSystemPromptAsync(CancellationToken cancellationToken)
    {
        var snippets = (await snippetStore.GetAllAsync(cancellationToken))
            .Where(snippet => snippet.IsActive)
            .OrderBy(snippet => snippet.Title)
            .ToList();

        if (snippets.Count == 0)
        {
            return _systemPrompt;
        }

        var builder = new StringBuilder(_systemPrompt);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Cac doan tin nhan mau duoc phep tham khao khi phu hop:");

        foreach (var snippet in snippets)
        {
            builder.Append("- ");
            builder.Append(snippet.Title);

            if (!string.IsNullOrWhiteSpace(snippet.Shortcut))
            {
                builder.Append(" (");
                builder.Append(snippet.Shortcut);
                builder.Append(')');
            }

            builder.Append(": ");
            builder.AppendLine(snippet.Content);
        }

        return builder.ToString();
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

static class AppConfig
{
    public static string Required(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing {key}");
}

static class AdminAuth
{
    public static bool IsAuthorized(HttpRequest request, IConfiguration config)
    {
        var adminToken = config["App:AdminToken"];

        if (string.IsNullOrWhiteSpace(adminToken))
        {
            return true;
        }

        var headerToken = request.Headers["X-Admin-Token"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(adminToken),
            Encoding.UTF8.GetBytes(headerToken));
    }
}

sealed class MessageSnippetStore(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _filePath = Path.Combine(environment.ContentRootPath, "data", "message-snippets.json");

    public async Task<IReadOnlyList<MessageSnippet>> GetAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            return await ReadUnlockedAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> CreateAsync(MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        var normalized = Normalize(input);

        if (normalized is null)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var snippets = await ReadUnlockedAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var snippet = new MessageSnippet(
                Guid.NewGuid().ToString("N"),
                normalized.Title,
                normalized.Shortcut,
                normalized.Content,
                normalized.IsActive,
                now,
                now);

            snippets.Add(snippet);
            await WriteUnlockedAsync(snippets, cancellationToken);
            return snippet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> UpdateAsync(string id, MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        var normalized = Normalize(input);

        if (normalized is null)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            var snippets = await ReadUnlockedAsync(cancellationToken);
            var index = snippets.FindIndex(snippet => snippet.Id == id);

            if (index < 0)
            {
                return null;
            }

            var existing = snippets[index];
            var updated = existing with
            {
                Title = normalized.Title,
                Shortcut = normalized.Shortcut,
                Content = normalized.Content,
                IsActive = normalized.IsActive,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            snippets[index] = updated;
            await WriteUnlockedAsync(snippets, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var snippets = await ReadUnlockedAsync(cancellationToken);
            var removed = snippets.RemoveAll(snippet => snippet.Id == id) > 0;

            if (removed)
            {
                await WriteUnlockedAsync(snippets, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<MessageSnippet>> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<MessageSnippet>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteUnlockedAsync(List<MessageSnippet> snippets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, snippets, JsonOptions, cancellationToken);
    }

    private static NormalizedMessageSnippet? Normalize(MessageSnippetUpsert input)
    {
        var title = input.Title?.Trim();
        var shortcut = input.Shortcut?.Trim();
        var content = input.Content?.Trim();

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return new NormalizedMessageSnippet(
            title.Length <= 120 ? title : title[..120],
            string.IsNullOrWhiteSpace(shortcut) ? null : shortcut,
            content.Length <= 2000 ? content : content[..2000],
            input.IsActive);
    }
}

sealed record NormalizedMessageSnippet(
    string Title,
    string? Shortcut,
    string Content,
    bool IsActive);

sealed record MessageSnippet(
    string Id,
    string Title,
    string? Shortcut,
    string Content,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

sealed record MessageSnippetUpsert(
    string? Title,
    string? Shortcut,
    string? Content,
    bool IsActive = true);

sealed class AppOptions
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

sealed class MessengerWebhookPayload
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    [JsonPropertyName("postback")]
    public MessengerPostback? Postback { get; set; }

    [JsonPropertyName("delivery")]
    public JsonElement? Delivery { get; set; }

    [JsonPropertyName("read")]
    public JsonElement? Read { get; set; }

    [JsonPropertyName("reaction")]
    public JsonElement? Reaction { get; set; }

    public bool IsIgnorable =>
        Message?.IsEcho == true ||
        Delivery.HasValue ||
        Read.HasValue ||
        Reaction.HasValue;

    public string? GetUserInput()
    {
        if (!string.IsNullOrWhiteSpace(Message?.QuickReply?.Payload))
        {
            return Message.QuickReply.Payload;
        }

        if (!string.IsNullOrWhiteSpace(Message?.Text))
        {
            return Message.Text;
        }

        if (!string.IsNullOrWhiteSpace(Postback?.Payload))
        {
            return Postback.Payload;
        }

        if (!string.IsNullOrWhiteSpace(Postback?.Title))
        {
            return Postback.Title;
        }

        if (Message?.Attachments?.Count > 0)
        {
            return "Nguoi dung vua gui tep dinh kem. Hay tra loi ngan gon rang minh hien chi xu ly duoc tin nhan van ban.";
        }

        return null;
    }
}

sealed class MessengerUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

sealed class MessengerMessage
{
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("is_echo")]
    public bool? IsEcho { get; set; }

    [JsonPropertyName("quick_reply")]
    public MessengerQuickReply? QuickReply { get; set; }

    [JsonPropertyName("attachments")]
    public List<MessengerAttachment>? Attachments { get; set; }
}

sealed class MessengerQuickReply
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

sealed class MessengerAttachment
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}

sealed class MessengerPostback
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}
