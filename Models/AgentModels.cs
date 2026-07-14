using System.Text.Json.Serialization;

public sealed record AgentMemory(
    string Id,
    string SenderId,
    string MemoryType,
    string Content,
    int Importance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AgentToolCallLog(
    string Id,
    string SenderId,
    string ToolName,
    string InputJson,
    string OutputJson,
    DateTimeOffset CreatedAt);

public sealed record AgentResult(
    string ReplyText,
    IReadOnlyList<AgentToolCallResult> ToolCalls,
    IReadOnlyList<AgentMemoryDraft> SavedMemories);

public sealed record AgentToolCallResult(
    string ToolName,
    string InputJson,
    string OutputJson);

public sealed record AgentDecision(
    [property: JsonPropertyName("reply")] string? Reply,
    [property: JsonPropertyName("memories_to_save")] List<AgentMemoryDraft>? MemoriesToSave,
    [property: JsonPropertyName("tools_to_call")] List<AgentToolRequest>? ToolsToCall);

public sealed record AgentMemoryDraft(
    [property: JsonPropertyName("memory_type")] string? MemoryType,
    [property: JsonPropertyName("content")] string? Content,
    [property: JsonPropertyName("importance")] int Importance = 1);

public sealed record AgentToolRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("input")] Dictionary<string, string>? Input);
