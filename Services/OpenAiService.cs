using System.Text;

public sealed class OpenAiService(
    OpenAiCompatibleChatModel chatModel,
    IConfiguration config,
    AppDatabase database)
{
    private readonly string _systemPrompt = config["App:SystemPrompt"]
        ?? "Ban la tro ly AI than thien cho fanpage Messenger. Tra loi ngan gon, tu nhien bang tieng Viet.";

    public async Task<string> CreateChatReplyAsync(string userText, CancellationToken cancellationToken)
    {
        var systemPrompt = await BuildSystemPromptAsync(cancellationToken);
        return await CreateChatReplyAsync(systemPrompt, userText, cancellationToken);
    }

    public async Task<string> CreateChatReplyAsync(string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        return await chatModel.CreateChatCompletionAsync(systemPrompt, userText, cancellationToken);
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

}
