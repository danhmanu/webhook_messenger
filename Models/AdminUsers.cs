public sealed record AdminUser(
    string Id,
    string Username,
    string DisplayName,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record AdminSession(
    string Id,
    string UserId,
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastSeenAt);

public sealed record AdminSessionCreated(
    string AccessToken,
    AdminSession Session);
