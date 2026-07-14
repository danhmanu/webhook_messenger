using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class MessengerWebhookPayload
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("entry")]
    public List<MessengerEntry>? Entry { get; set; }
}

public sealed class MessengerEntry
{
    [JsonPropertyName("messaging")]
    public List<MessengerMessaging>? Messaging { get; set; }
}

public sealed class MessengerMessaging
{
    [JsonPropertyName("sender")]
    public MessengerUser? Sender { get; set; }

    [JsonPropertyName("message")]
    public MessengerMessage? Message { get; set; }

    [JsonPropertyName("postback")]
    public MessengerPostback? Postback { get; set; }

    [JsonPropertyName("delivery")]
    public JsonElement? Delivery { get; set; }

    [JsonPropertyName("read")]
    public JsonElement? Read { get; set; }

    [JsonPropertyName("reaction")]
    public JsonElement? Reaction { get; set; }

    public bool IsIgnorable =>
        Message?.IsEcho == true ||
        Delivery.HasValue ||
        Read.HasValue ||
        Reaction.HasValue;

    public string EventType
    {
        get
        {
            if (Message?.IsEcho == true)
            {
                return "message_echo";
            }

            if (Message is not null)
            {
                return Message.Attachments?.Count > 0 ? "message_attachment" : "message";
            }

            if (Postback is not null)
            {
                return "postback";
            }

            if (Delivery.HasValue)
            {
                return "delivery";
            }

            if (Read.HasValue)
            {
                return "read";
            }

            if (Reaction.HasValue)
            {
                return "reaction";
            }

            return "unknown";
        }
    }

    public string? GetUserInput()
    {
        if (!string.IsNullOrWhiteSpace(Message?.QuickReply?.Payload))
        {
            return Message.QuickReply.Payload;
        }

        if (!string.IsNullOrWhiteSpace(Message?.Text))
        {
            return Message.Text;
        }

        if (!string.IsNullOrWhiteSpace(Postback?.Payload))
        {
            return Postback.Payload;
        }

        if (!string.IsNullOrWhiteSpace(Postback?.Title))
        {
            return Postback.Title;
        }

        if (Message?.Attachments?.Count > 0)
        {
            return "Nguoi dung vua gui tep dinh kem. Hay tra loi ngan gon rang minh hien chi xu ly duoc tin nhan van ban.";
        }

        return null;
    }
}

public sealed class MessengerUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class MessengerMessage
{
    [JsonPropertyName("mid")]
    public string? Mid { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("is_echo")]
    public bool? IsEcho { get; set; }

    [JsonPropertyName("quick_reply")]
    public MessengerQuickReply? QuickReply { get; set; }

    [JsonPropertyName("attachments")]
    public List<MessengerAttachment>? Attachments { get; set; }
}

public sealed class MessengerQuickReply
{
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}

public sealed class MessengerAttachment
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; set; }
}

public sealed class MessengerPostback
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
}
