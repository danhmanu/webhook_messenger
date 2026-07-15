using System.Text;
using System.Text.Json;

public sealed class HospitalChatModel(HttpClient httpClient, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _baseUrl = (config["App:OpenAiBaseUrl"] ?? "http://10.0.0.142:5000/api").TrimEnd('/');

    public async Task<string> CreateChatCompletionAsync(
        string systemPrompt,
        string userText,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat");

        var body = new
        {
            question = userText
        };

        request.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hospital chat API failed: {(int)response.StatusCode} {responseText}");
        }

        using var document = JsonDocument.Parse(responseText);
        return ExtractChatText(document.RootElement) ?? "Minh chua co cau tra loi phu hop.";
    }

    private static string? ExtractChatText(JsonElement root)
    {
        if (TryGetString(root, "answer", out var answer))
        {
            return CleanModelText(answer);
        }

        if (TryGetString(root, "response", out var response))
        {
            return CleanModelText(response);
        }

        if (TryGetString(root, "message", out var messageText))
        {
            return CleanModelText(messageText);
        }

        return ExtractOpenAiCompatibleText(root);
    }

    private static string? ExtractOpenAiCompatibleText(JsonElement root)
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

    private static bool TryGetString(JsonElement root, string propertyName, out string? value)
    {
        value = null;

        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
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
