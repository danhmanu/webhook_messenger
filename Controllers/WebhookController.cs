using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("webhook")]
public sealed class WebhookController(
    IConfiguration config,
    MessengerWebhookVerifier verifier,
    AgentService agent,
    MessengerService messenger,
    AppDatabase database,
    ILogger<WebhookController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify()
    {
        var mode = Request.Query["hub.mode"].ToString();
        var token = Request.Query["hub.verify_token"].ToString();
        var challenge = Request.Query["hub.challenge"].ToString();
        var verifyToken = config["App:MessengerVerifyToken"];

        if (mode == "subscribe" && token == verifyToken)
        {
            return Content(challenge, "text/plain", Encoding.UTF8);
        }

        return Unauthorized();
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (!verifier.IsValid(Request, body))
        {
            logger.LogWarning("Rejected webhook because X-Hub-Signature-256 is invalid or missing");
            return Unauthorized();
        }

        MessengerWebhookPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<MessengerWebhookPayload>(body, MessengerWebhookPayload.JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Rejected webhook because payload JSON is invalid");
            return BadRequest();
        }

        if (payload?.Object != "page")
        {
            logger.LogInformation("Ignored webhook object {Object}", payload?.Object ?? "null");
            return Ok();
        }

        var entryCount = payload.Entry?.Count ?? 0;
        logger.LogInformation("Received Messenger webhook with {EntryCount} entries", entryCount);

        foreach (var entry in payload.Entry ?? [])
        {
            foreach (var messaging in entry.Messaging ?? [])
            {
                await HandleMessagingEventAsync(messaging, cancellationToken);
            }
        }

        return Ok();
    }

    private async Task HandleMessagingEventAsync(MessengerMessaging messaging, CancellationToken cancellationToken)
    {
        var senderId = messaging.Sender?.Id;
        var text = messaging.GetUserInput();

        if (messaging.IsIgnorable)
        {
            logger.LogInformation(
                "Ignored Messenger event {EventType} from sender {SenderId}",
                messaging.EventType,
                senderId ?? "unknown");
            return;
        }

        if (string.IsNullOrWhiteSpace(senderId))
        {
            logger.LogWarning("Ignored Messenger event {EventType} because sender id is missing", messaging.EventType);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogInformation("Ignored Messenger event {EventType} from sender {SenderId} because it has no text input", messaging.EventType, senderId);
            return;
        }

        try
        {
            logger.LogInformation(
                "Handling Messenger message from sender {SenderId}: {MessagePreview}",
                senderId,
                TextPreview.ForLog(text));

            await database.AddConversationMessageAsync(
                senderId,
                "inbound",
                "user",
                text,
                messaging.EventType,
                messaging.Message?.Mid,
                true,
                cancellationToken);

            await messenger.SendTypingOnAsync(senderId, cancellationToken);
            var agentResult = await agent.ProcessAsync(senderId, text, cancellationToken);
            var answer = agentResult.ReplyText;
            await messenger.SendTextAsync(senderId, answer, cancellationToken);
            await database.AddConversationMessageAsync(
                senderId,
                "outbound",
                "bot",
                answer,
                "bot_reply",
                null,
                false,
                cancellationToken);

            logger.LogInformation(
                "Sent Messenger reply to sender {SenderId}: {ReplyPreview}",
                senderId,
                TextPreview.ForLog(answer));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle message from sender {SenderId}", senderId);

            try
            {
                const string fallbackText = "Xin loi, hien tai minh chua tra loi duoc. Ban thu lai sau nhe.";
                await messenger.SendTextAsync(senderId, fallbackText, cancellationToken);
                await database.AddConversationMessageAsync(
                    senderId,
                    "outbound",
                    "bot",
                    fallbackText,
                    "fallback_reply",
                    null,
                    false,
                    cancellationToken);
            }
            catch (Exception sendError)
            {
                logger.LogError(sendError, "Failed to send fallback message to sender {SenderId}", senderId);
            }
        }
    }
}
