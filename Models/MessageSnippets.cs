public sealed record MessageSnippet(
    string Id,
    string Title,
    string? Shortcut,
    string Content,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MessageSnippetUpsert(
    string? Title,
    string? Shortcut,
    string? Content,
    bool IsActive = true);

public sealed record MessageSnippetActivation(bool IsActive);

public sealed record NormalizedMessageSnippet(
    string Title,
    string? Shortcut,
    string Content,
    bool IsActive);
