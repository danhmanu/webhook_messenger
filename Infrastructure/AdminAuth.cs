using System.Security.Cryptography;
using System.Text;

public static class AdminAuth
{
    public static bool IsAuthorized(HttpRequest request, IConfiguration config)
    {
        var adminToken = config["App:AdminToken"];

        if (string.IsNullOrWhiteSpace(adminToken))
        {
            return true;
        }

        var headerToken = request.Headers["X-Admin-Token"].ToString();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(adminToken),
            Encoding.UTF8.GetBytes(headerToken));
    }
}
