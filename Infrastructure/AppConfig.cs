public static class AppConfig
{
    public static string Required(string? value, string key) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing {key}");
}
