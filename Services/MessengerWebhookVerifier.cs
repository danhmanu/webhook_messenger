using System.Security.Cryptography;
using System.Text;

public sealed class MessengerWebhookVerifier(IConfiguration config)
{
    private readonly string? _appSecret = config["App:MessengerAppSecret"];

    public bool IsValid(HttpRequest request, string body)
    {
        if (string.IsNullOrWhiteSpace(_appSecret))
        {
            return true;
        }

        var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();

        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_appSecret),
            Encoding.UTF8.GetBytes(body));

        var expectedSignature = $"sha256={Convert.ToHexString(expectedBytes).ToLowerInvariant()}";
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
    }
}
