public static class TextPreview
{
    public static string ForLog(string text)
    {
        const int maxLength = 180;
        var normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }
}
