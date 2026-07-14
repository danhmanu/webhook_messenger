using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class OpenAiCompatibleChatModel(HttpClient httpClient, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // private readonly string _apiKey = config["App:OpenAiApiKey"] ?? "dummy";
    // private readonly string _baseUrl = (config["App:OpenAiBaseUrl"] ?? "http://chatbot.bvdkgiadinh.com:5000/api").TrimEnd('/');
    // private readonly string _model = config["App:OpenAiModel"] ?? "qwen3-8b";
    // private readonly double _temperature = config.GetValue<double?>("App:OpenAiTemperature") ?? 0.7;
    // private readonly int _maxTokens = config.GetValue<int?>("App:OpenAiMaxTokens") ?? 512;
    private readonly string _apiKey =  "dummy";
    private readonly string _baseUrl =  "http://chatbot.bvdkgiadinh.com:5000/api";
    private readonly string _model = "qwen3-8b";
    private readonly double _temperature =  0.7;
    private readonly int _maxTokens = 512;

    public async Task<string> CreateChatCompletionAsync(
        string systemPrompt,
        string userText,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat");
        // request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        var body = new
        {
            model = _model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content = userText
                }
            },
            temperature = _temperature,
            max_tokens = _maxTokens
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Chat completion API failed: {(int)response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        return ExtractChatCompletionText(document.RootElement) ?? "Minh chua co cau tra loi phu hop.";
    }

    private static string? ExtractChatCompletionText(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return null;
        }

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        return content.ValueKind switch
        {
            JsonValueKind.String => CleanModelText(content.GetString()),
            JsonValueKind.Array => CleanModelText(ExtractTextFromContentArray(content)),
            _ => null
        };
    }

    private static string? ExtractTextFromContentArray(JsonElement content)
    {
        var builder = new StringBuilder();

        foreach (var item in content.EnumerateArray())
        {
            if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString().Trim();
    }

    private static string? CleanModelText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim();

        while (cleaned.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
        {
            var end = cleaned.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);

            if (end < 0)
            {
                return null;
            }

            cleaned = cleaned[(end + "</think>".Length)..].Trim();
        }

        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }
}
