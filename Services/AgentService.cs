using System.Text;
using System.Text.Json;

public sealed class AgentService(
    OpenAiService openAi,
    AppDatabase database,
    ILogger<AgentService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<AgentResult> ProcessAsync(string senderId, string userText, CancellationToken cancellationToken)
    {
        var reply = (await openAi.CreateChatReplyAsync("", userText, cancellationToken)).Trim();

        if (string.IsNullOrWhiteSpace(reply))
        {
            reply = "Cam on ban da nhan tin. Minh da ghi nhan thong tin va se ho tro ngay.";
        }

        logger.LogInformation(
            "Agent processed sender {SenderId} with one chat API call",
            senderId);

        return new AgentResult(reply, [], []);
    }

    private static string BuildSystemPrompt(
        IReadOnlyList<AgentMemory> memories,
        IEnumerable<ConversationMessage> history,
        IReadOnlyList<MessageSnippet> snippets)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ban la AI Agent xu ly tin nhan Facebook Messenger truoc khi phan hoi.");
        builder.AppendLine("Hay tra loi ngan gon, tu nhien bang tieng Viet.");
        builder.AppendLine("Uu tien tin nhan mau roi toi thong tin trong memory va lich su hoi thoai.");
        builder.AppendLine("Neu thieu thong tin quan trong, hoi lai mot cau ro rang.");
        builder.AppendLine("Khong bia dat gia, lich hen, chinh sach neu khong co du lieu.");
        builder.AppendLine();
        builder.AppendLine("Chi tra ve JSON hop le, khong markdown, theo schema:");
        builder.AppendLine("""
            {
              "reply": "noi dung tra loi cho khach",
              "memories_to_save": [
                { "memory_type": "customer_profile|preference|intent|note", "content": "thong tin can nho", "importance": 1 }
              ],
              "tools_to_call": [
                { "name": "search_snippets|get_memory|get_conversation_history|save_memory", "input": { "query": "tu khoa", "content": "noi dung" } }
              ]
            }
            """);
        builder.AppendLine("Neu khong can tool hay memory, tra mang rong.");
        builder.AppendLine();
        builder.AppendLine("Tools noi bo:");
        builder.AppendLine("- search_snippets: tim tin nhan mau dang bat trong SQLite theo input.query.");
        builder.AppendLine("- get_memory: doc memory dai han theo nguoi dung.");
        builder.AppendLine("- get_conversation_history: doc cac tin gan nhat.");
        builder.AppendLine("- save_memory: luu memory neu co thong tin huu ich lau dai.");
        builder.AppendLine();
        builder.AppendLine("Memory hien co:");
        AppendList(builder, memories.Select(memory => $"- [{memory.MemoryType}, importance {memory.Importance}] {memory.Content}"));
        builder.AppendLine();
        builder.AppendLine("Lich su hoi thoai gan day:");
        AppendList(builder, history.Select(message => $"- {message.Source}/{message.Direction}: {message.Text}"));
        builder.AppendLine();
        builder.AppendLine("Tin nhan mau phu hop:");
        AppendList(builder, snippets.Select(snippet => $"- {snippet.Title}{(string.IsNullOrWhiteSpace(snippet.Shortcut) ? "" : $" ({snippet.Shortcut})")}: {snippet.Content}"));

        return builder.ToString();
    }

    private static string BuildToolFollowUpPrompt(
        string basePrompt,
        string userText,
        IReadOnlyList<AgentToolCallResult> toolResults)
    {
        var builder = new StringBuilder(basePrompt);
        builder.AppendLine();
        builder.AppendLine("Ket qua tool da chay:");

        foreach (var result in toolResults)
        {
            builder.AppendLine($"- Tool: {result.ToolName}");
            builder.AppendLine($"  Input: {result.InputJson}");
            builder.AppendLine($"  Output: {result.OutputJson}");
        }

        builder.AppendLine();
        builder.AppendLine("Hay dua ra cau tra loi cuoi cung dua tren ket qua tool.");
        builder.AppendLine("Van chi tra ve JSON hop le theo schema da neu.");
        builder.AppendLine($"Tin nhan nguoi dung: {userText}");
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, IEnumerable<string> lines)
    {
        var count = 0;

        foreach (var line in lines)
        {
            builder.AppendLine(line);
            count++;
        }

        if (count == 0)
        {
            builder.AppendLine("- Khong co");
        }
    }

    private static AgentDecision ParseDecision(string modelOutput)
    {
        var json = ExtractJson(modelOutput);

        try
        {
            return JsonSerializer.Deserialize<AgentDecision>(json, JsonOptions)
                ?? new AgentDecision(modelOutput, [], []);
        }
        catch (JsonException)
        {
            return new AgentDecision(modelOutput, [], []);
        }
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');

        return start >= 0 && end > start ? trimmed[start..(end + 1)] : trimmed;
    }

    private async Task<AgentToolCallResult?> ExecuteToolAsync(
        string senderId,
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var toolName = request.Name?.Trim();

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return null;
        }

        var inputJson = JsonSerializer.Serialize(request.Input ?? [], JsonOptions);
        object output = toolName switch
        {
            "search_snippets" => await database.SearchActiveSnippetsAsync(GetInput(request, "query"), cancellationToken),
            "get_memory" => await database.GetAgentMemoriesAsync(senderId, cancellationToken),
            "get_conversation_history" => await database.GetConversationMessagesAsync(senderId, cancellationToken),
            "save_memory" => await SaveMemoryToolAsync(senderId, request, cancellationToken),
            _ => new { error = "unknown_tool", tool = toolName }
        };

        var outputJson = JsonSerializer.Serialize(output, JsonOptions);
        await database.AddAgentToolCallAsync(senderId, toolName, inputJson, outputJson, cancellationToken);

        return new AgentToolCallResult(toolName, inputJson, outputJson);
    }

    private async Task<object> SaveMemoryToolAsync(
        string senderId,
        AgentToolRequest request,
        CancellationToken cancellationToken)
    {
        var content = GetInput(request, "content");

        if (string.IsNullOrWhiteSpace(content))
        {
            return new { saved = false, error = "content_required" };
        }

        var memory = await database.SaveAgentMemoryAsync(
            senderId,
            new AgentMemoryDraft(GetInput(request, "memory_type", "note"), content, 2),
            cancellationToken);

        return new { saved = memory is not null, memory };
    }

    private static string GetInput(AgentToolRequest request, string key, string fallback = "") =>
        request.Input is not null && request.Input.TryGetValue(key, out var value) ? value : fallback;
}
