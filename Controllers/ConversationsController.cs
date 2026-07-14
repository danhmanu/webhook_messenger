using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/v1/conversations")]
public sealed class ConversationsController(
    AppDatabase database,
    MessengerService messenger,
    IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!AdminAuth.IsAuthorized(Request, config))
        {
            return Unauthorized();
        }

        return Ok(await database.GetConversationsAsync(cancellationToken));
    }

    [HttpGet("{senderId}/messages")]
    public async Task<IActionResult> ListMessages(string senderId, CancellationToken cancellationToken)
    {
        if (!AdminAuth.IsAuthorized(Request, config))
        {
            return Unauthorized();
        }

        await database.MarkConversationReadAsync(senderId, cancellationToken);
        return Ok(await database.GetConversationMessagesAsync(senderId, cancellationToken));
    }

    [HttpGet("{senderId}/agent-memories")]
    public async Task<IActionResult> ListAgentMemories(string senderId, CancellationToken cancellationToken)
    {
        if (!AdminAuth.IsAuthorized(Request, config))
        {
            return Unauthorized();
        }

        return Ok(await database.GetAgentMemoriesAsync(senderId, cancellationToken));
    }

    [HttpGet("{senderId}/agent-tool-calls")]
    public async Task<IActionResult> ListAgentToolCalls(string senderId, CancellationToken cancellationToken)
    {
        if (!AdminAuth.IsAuthorized(Request, config))
        {
            return Unauthorized();
        }

        return Ok(await database.GetAgentToolCallsAsync(senderId, cancellationToken));
    }

    [HttpPost("{senderId}/messages")]
    public async Task<IActionResult> SendMessage(string senderId, [FromBody] SendConversationMessage input, CancellationToken cancellationToken)
    {
        if (!AdminAuth.IsAuthorized(Request, config))
        {
            return Unauthorized();
        }

        var text = input.Text?.Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "text_required" });
        }

        await messenger.SendTextAsync(senderId, text, cancellationToken);
        var message = await database.AddConversationMessageAsync(
            senderId,
            "outbound",
            "admin",
            text,
            "manual_reply",
            null,
            false,
            cancellationToken);

        return Created($"/api/v1/conversations/{senderId}/messages/{message.Id}", message);
    }
}
