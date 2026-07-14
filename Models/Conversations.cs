public sealed record ConversationSummary(
    string SenderId,
    string? DisplayName,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    int UnreadCount,
    DateTimeOffset UpdatedAt);

public sealed record ConversationMessage(
    string Id,
    string SenderId,
    string Direction,
    string Source,
    string Text,
    string EventType,
    string? FacebookMessageId,
    DateTimeOffset CreatedAt);

public sealed record SendConversationMessage(string? Text);
