public static class AdminAuth
{
    public static string GetToken(HttpRequest request) => request.Headers["X-Admin-Token"].ToString();

    public static async Task<AdminSession?> GetSessionAsync(
        HttpRequest request,
        AppDatabase database,
        CancellationToken cancellationToken) =>
        await database.ValidateAdminSessionAsync(GetToken(request), cancellationToken);
}
