namespace EventCalendarCollector.BLL.Publishers.Google;

public static class GoogleCalendarExtensions
{
    // Google Calendar event IDs must be 5-1024 chars, [a-v0-9] only.
    // Hex chars (0-9, a-f) are a valid subset of that alphabet.
    public static string ToGoogleEventId(this string sourceId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sourceId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
