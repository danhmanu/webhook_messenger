using System.Text.Json;
using Microsoft.Data.Sqlite;

public sealed class AppDatabase(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _databasePath = Path.Combine(environment.ContentRootPath, "data", "messenger-webhook.db");
    private readonly string _legacySnippetPath = Path.Combine(environment.ContentRootPath, "data", "message-snippets.json");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            await using var connection = OpenConnection();
            await ExecuteNonQueryAsync(connection, """
                CREATE TABLE IF NOT EXISTS message_snippets (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL,
                    shortcut TEXT NULL,
                    content TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS conversations (
                    sender_id TEXT PRIMARY KEY,
                    display_name TEXT NULL,
                    last_message_preview TEXT NOT NULL,
                    last_message_at TEXT NOT NULL,
                    unread_count INTEGER NOT NULL DEFAULT 0,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS conversation_messages (
                    id TEXT PRIMARY KEY,
                    sender_id TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    source TEXT NOT NULL,
                    text TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    facebook_message_id TEXT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(sender_id) REFERENCES conversations(sender_id)
                );

                CREATE INDEX IF NOT EXISTS ix_conversation_messages_sender_created
                    ON conversation_messages(sender_id, created_at);

                CREATE INDEX IF NOT EXISTS ix_conversations_last_message_at
                    ON conversations(last_message_at DESC);

                CREATE TABLE IF NOT EXISTS agent_memories (
                    id TEXT PRIMARY KEY,
                    sender_id TEXT NOT NULL,
                    memory_type TEXT NOT NULL,
                    content TEXT NOT NULL,
                    importance INTEGER NOT NULL DEFAULT 1,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_agent_memories_sender_updated
                    ON agent_memories(sender_id, updated_at DESC);

                CREATE TABLE IF NOT EXISTS agent_tool_calls (
                    id TEXT PRIMARY KEY,
                    sender_id TEXT NOT NULL,
                    tool_name TEXT NOT NULL,
                    input_json TEXT NOT NULL,
                    output_json TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_agent_tool_calls_sender_created
                    ON agent_tool_calls(sender_id, created_at DESC);
                """, cancellationToken);

            await ImportLegacySnippetsIfNeededAsync(connection, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<MessageSnippet>> GetAllSnippetsAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, shortcut, content, is_active, created_at, updated_at
                FROM message_snippets
                ORDER BY updated_at DESC;
                """;

            var snippets = new List<MessageSnippet>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                snippets.Add(ReadSnippet(reader));
            }

            return snippets;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> GetSnippetByIdAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            return await GetSnippetByIdUnlockedAsync(connection, id, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> CreateSnippetAsync(MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        var normalized = Normalize(input);

        if (normalized is null)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var now = DateTimeOffset.UtcNow;
            var snippet = new MessageSnippet(
                Guid.NewGuid().ToString("N"),
                normalized.Title,
                normalized.Shortcut,
                normalized.Content,
                normalized.IsActive,
                now,
                now);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO message_snippets (id, title, shortcut, content, is_active, created_at, updated_at)
                VALUES ($id, $title, $shortcut, $content, $isActive, $createdAt, $updatedAt);
                """;
            AddParameter(command, "$id", snippet.Id);
            AddParameter(command, "$title", snippet.Title);
            AddParameter(command, "$shortcut", snippet.Shortcut);
            AddParameter(command, "$content", snippet.Content);
            AddParameter(command, "$isActive", snippet.IsActive ? 1 : 0);
            AddParameter(command, "$createdAt", ToStorage(snippet.CreatedAt));
            AddParameter(command, "$updatedAt", ToStorage(snippet.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return snippet;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> UpdateSnippetAsync(string id, MessageSnippetUpsert input, CancellationToken cancellationToken)
    {
        var normalized = Normalize(input);

        if (normalized is null)
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var existing = await GetSnippetByIdUnlockedAsync(connection, id, cancellationToken);

            if (existing is null)
            {
                return null;
            }

            var updated = existing with
            {
                Title = normalized.Title,
                Shortcut = normalized.Shortcut,
                Content = normalized.Content,
                IsActive = normalized.IsActive,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE message_snippets
                SET title = $title,
                    shortcut = $shortcut,
                    content = $content,
                    is_active = $isActive,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            AddParameter(command, "$id", updated.Id);
            AddParameter(command, "$title", updated.Title);
            AddParameter(command, "$shortcut", updated.Shortcut);
            AddParameter(command, "$content", updated.Content);
            AddParameter(command, "$isActive", updated.IsActive ? 1 : 0);
            AddParameter(command, "$updatedAt", ToStorage(updated.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<MessageSnippet?> SetSnippetActiveAsync(string id, bool isActive, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var existing = await GetSnippetByIdUnlockedAsync(connection, id, cancellationToken);

            if (existing is null)
            {
                return null;
            }

            var updated = existing with
            {
                IsActive = isActive,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE message_snippets
                SET is_active = $isActive,
                    updated_at = $updatedAt
                WHERE id = $id;
                """;
            AddParameter(command, "$id", updated.Id);
            AddParameter(command, "$isActive", updated.IsActive ? 1 : 0);
            AddParameter(command, "$updatedAt", ToStorage(updated.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteSnippetAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM message_snippets WHERE id = $id;";
            AddParameter(command, "$id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT sender_id, display_name, last_message_preview, last_message_at, unread_count, updated_at
                FROM conversations
                ORDER BY last_message_at DESC;
                """;

            var conversations = new List<ConversationSummary>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                conversations.Add(new ConversationSummary(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    FromStorage(reader.GetString(3)),
                    reader.GetInt32(4),
                    FromStorage(reader.GetString(5))));
            }

            return conversations;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetConversationMessagesAsync(string senderId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            return await GetConversationMessagesUnlockedAsync(connection, senderId, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkConversationReadAsync(string senderId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE conversations
                SET unread_count = 0,
                    updated_at = $updatedAt
                WHERE sender_id = $senderId;
                """;
            AddParameter(command, "$senderId", senderId);
            AddParameter(command, "$updatedAt", ToStorage(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ConversationMessage> AddConversationMessageAsync(
        string senderId,
        string direction,
        string source,
        string text,
        string eventType,
        string? facebookMessageId,
        bool countAsUnread,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var now = DateTimeOffset.UtcNow;
            var message = new ConversationMessage(
                Guid.NewGuid().ToString("N"),
                senderId,
                direction,
                source,
                text,
                eventType,
                facebookMessageId,
                now);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            await using (var upsertConversation = connection.CreateCommand())
            {
                upsertConversation.Transaction = (SqliteTransaction)transaction;
                upsertConversation.CommandText = """
                    INSERT INTO conversations
                        (sender_id, display_name, last_message_preview, last_message_at, unread_count, updated_at)
                    VALUES
                        ($senderId, NULL, $preview, $lastMessageAt, $unreadCount, $updatedAt)
                    ON CONFLICT(sender_id) DO UPDATE SET
                        last_message_preview = excluded.last_message_preview,
                        last_message_at = excluded.last_message_at,
                        unread_count = conversations.unread_count + excluded.unread_count,
                        updated_at = excluded.updated_at;
                    """;
                AddParameter(upsertConversation, "$senderId", senderId);
                AddParameter(upsertConversation, "$preview", TextPreview.ForLog(text));
                AddParameter(upsertConversation, "$lastMessageAt", ToStorage(now));
                AddParameter(upsertConversation, "$unreadCount", countAsUnread ? 1 : 0);
                AddParameter(upsertConversation, "$updatedAt", ToStorage(now));
                await upsertConversation.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insertMessage = connection.CreateCommand())
            {
                insertMessage.Transaction = (SqliteTransaction)transaction;
                insertMessage.CommandText = """
                    INSERT INTO conversation_messages
                        (id, sender_id, direction, source, text, event_type, facebook_message_id, created_at)
                    VALUES
                        ($id, $senderId, $direction, $source, $text, $eventType, $facebookMessageId, $createdAt);
                    """;
                AddParameter(insertMessage, "$id", message.Id);
                AddParameter(insertMessage, "$senderId", message.SenderId);
                AddParameter(insertMessage, "$direction", message.Direction);
                AddParameter(insertMessage, "$source", message.Source);
                AddParameter(insertMessage, "$text", message.Text);
                AddParameter(insertMessage, "$eventType", message.EventType);
                AddParameter(insertMessage, "$facebookMessageId", message.FacebookMessageId);
                AddParameter(insertMessage, "$createdAt", ToStorage(message.CreatedAt));
                await insertMessage.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return message;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AgentMemory>> GetAgentMemoriesAsync(string senderId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, sender_id, memory_type, content, importance, created_at, updated_at
                FROM agent_memories
                WHERE sender_id = $senderId
                ORDER BY importance DESC, updated_at DESC
                LIMIT 20;
                """;
            AddParameter(command, "$senderId", senderId);

            var memories = new List<AgentMemory>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                memories.Add(new AgentMemory(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    FromStorage(reader.GetString(5)),
                    FromStorage(reader.GetString(6))));
            }

            return memories;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgentMemory?> SaveAgentMemoryAsync(
        string senderId,
        AgentMemoryDraft draft,
        CancellationToken cancellationToken)
    {
        var memoryType = draft.MemoryType?.Trim();
        var content = draft.Content?.Trim();

        if (string.IsNullOrWhiteSpace(memoryType) || string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var now = DateTimeOffset.UtcNow;
            var memory = new AgentMemory(
                Guid.NewGuid().ToString("N"),
                senderId,
                memoryType.Length <= 80 ? memoryType : memoryType[..80],
                content.Length <= 1000 ? content : content[..1000],
                Math.Clamp(draft.Importance, 1, 5),
                now,
                now);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO agent_memories
                    (id, sender_id, memory_type, content, importance, created_at, updated_at)
                VALUES
                    ($id, $senderId, $memoryType, $content, $importance, $createdAt, $updatedAt);
                """;
            AddParameter(command, "$id", memory.Id);
            AddParameter(command, "$senderId", memory.SenderId);
            AddParameter(command, "$memoryType", memory.MemoryType);
            AddParameter(command, "$content", memory.Content);
            AddParameter(command, "$importance", memory.Importance);
            AddParameter(command, "$createdAt", ToStorage(memory.CreatedAt));
            AddParameter(command, "$updatedAt", ToStorage(memory.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return memory;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<AgentToolCallLog> AddAgentToolCallAsync(
        string senderId,
        string toolName,
        string inputJson,
        string outputJson,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            var now = DateTimeOffset.UtcNow;
            var log = new AgentToolCallLog(
                Guid.NewGuid().ToString("N"),
                senderId,
                toolName,
                inputJson,
                outputJson,
                now);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO agent_tool_calls
                    (id, sender_id, tool_name, input_json, output_json, created_at)
                VALUES
                    ($id, $senderId, $toolName, $inputJson, $outputJson, $createdAt);
                """;
            AddParameter(command, "$id", log.Id);
            AddParameter(command, "$senderId", log.SenderId);
            AddParameter(command, "$toolName", log.ToolName);
            AddParameter(command, "$inputJson", log.InputJson);
            AddParameter(command, "$outputJson", log.OutputJson);
            AddParameter(command, "$createdAt", ToStorage(log.CreatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return log;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<AgentToolCallLog>> GetAgentToolCallsAsync(string senderId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, sender_id, tool_name, input_json, output_json, created_at
                FROM agent_tool_calls
                WHERE sender_id = $senderId
                ORDER BY created_at DESC
                LIMIT 50;
                """;
            AddParameter(command, "$senderId", senderId);

            var logs = new List<AgentToolCallLog>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                logs.Add(new AgentToolCallLog(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    FromStorage(reader.GetString(5))));
            }

            return logs;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<MessageSnippet>> SearchActiveSnippetsAsync(string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();

        await _lock.WaitAsync(cancellationToken);

        try
        {
            await using var connection = OpenConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, shortcut, content, is_active, created_at, updated_at
                FROM message_snippets
                WHERE is_active = 1
                  AND (
                    $query = ''
                    OR lower(title) LIKE $pattern
                    OR lower(COALESCE(shortcut, '')) LIKE $pattern
                    OR lower(content) LIKE $pattern
                  )
                ORDER BY updated_at DESC
                LIMIT 8;
                """;
            AddParameter(command, "$query", normalizedQuery);
            AddParameter(command, "$pattern", $"%{normalizedQuery.ToLowerInvariant()}%");

            var snippets = new List<MessageSnippet>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                snippets.Add(ReadSnippet(reader));
            }

            return snippets;
        }
        finally
        {
            _lock.Release();
        }
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

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_databasePath}");
        connection.Open();
        return connection;
    }

    private async Task ImportLegacySnippetsIfNeededAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!File.Exists(_legacySnippetPath))
        {
            return;
        }

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM message_snippets;";
        var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        if (count > 0)
        {
            return;
        }

        await using var stream = File.OpenRead(_legacySnippetPath);
        var snippets = await JsonSerializer.DeserializeAsync<List<MessageSnippet>>(stream, JsonOptions, cancellationToken) ?? [];

        foreach (var snippet in snippets)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO message_snippets (id, title, shortcut, content, is_active, created_at, updated_at)
                VALUES ($id, $title, $shortcut, $content, $isActive, $createdAt, $updatedAt);
                """;
            AddParameter(command, "$id", snippet.Id);
            AddParameter(command, "$title", snippet.Title);
            AddParameter(command, "$shortcut", snippet.Shortcut);
            AddParameter(command, "$content", snippet.Content);
            AddParameter(command, "$isActive", snippet.IsActive ? 1 : 0);
            AddParameter(command, "$createdAt", ToStorage(snippet.CreatedAt));
            AddParameter(command, "$updatedAt", ToStorage(snippet.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<MessageSnippet?> GetSnippetByIdUnlockedAsync(SqliteConnection connection, string id, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, shortcut, content, is_active, created_at, updated_at
            FROM message_snippets
            WHERE id = $id;
            """;
        AddParameter(command, "$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSnippet(reader) : null;
    }

    private static async Task<IReadOnlyList<ConversationMessage>> GetConversationMessagesUnlockedAsync(
        SqliteConnection connection,
        string senderId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sender_id, direction, source, text, event_type, facebook_message_id, created_at
            FROM conversation_messages
            WHERE sender_id = $senderId
            ORDER BY created_at ASC;
            """;
        AddParameter(command, "$senderId", senderId);

        var messages = new List<ConversationMessage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new ConversationMessage(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                FromStorage(reader.GetString(7))));
        }

        return messages;
    }

    private static MessageSnippet ReadSnippet(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4) == 1,
            FromStorage(reader.GetString(5)),
            FromStorage(reader.GetString(6)));

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static string ToStorage(DateTimeOffset value) => value.UtcDateTime.ToString("O");

    private static DateTimeOffset FromStorage(string value) => DateTimeOffset.Parse(value);
}
