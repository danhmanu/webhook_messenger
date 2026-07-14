using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class OpenAiService(HttpClient httpClient, IConfiguration config, AppDatabase database)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string? _apiKey = config["App:OpenAiApiKey"];
    private readonly string _model = config["App:OpenAiModel"] ?? "gpt-4o-mini";
    private readonly string _systemPrompt = config["App:SystemPrompt"]
        ?? "Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet.";

    public async Task<string> CreateChatReplyAsync(string userText, CancellationToken cancellationToken)
    {
        var systemPrompt = await BuildSystemPromptAsync(cancellationToken);
        return await CreateChatReplyAsync(systemPrompt, userText, cancellationToken);
    }

    public async Task<string> CreateChatReplyAsync(string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        // var apiKey = AppConfig.Required(_apiKey, "App:OpenAiApiKey");
        var apiKey = "dummy";

        // using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://chatbot.bvdkgiadinh.com:8035/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var body = new
        {
            // model = _model,
            model = "qwen3-8b",
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
        var snippets = (await database.GetAllSnippetsAsync(cancellationToken))
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
