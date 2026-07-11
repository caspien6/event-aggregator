using System.Text.Json;
using System.Text.RegularExpressions;

namespace EventCalendarCollector.Web.Scrapers.Britishmood;

/// <summary>
/// Extracts event data from Facebook's server-rendered page HTML (as served to
/// crawler user agents). The event objects live inside large inline JSON blobs,
/// so this parser scans for the event nodes and brace-matches them out instead
/// of parsing the DOM.
/// </summary>
public partial class BritishmoodEventParser
{
    private readonly ILogger<BritishmoodEventParser> _logger;

    public BritishmoodEventParser(ILogger<BritishmoodEventParser> logger)
    {
        _logger = logger;
    }

    public record FacebookEventStub(
        string Id,
        string Name,
        bool IsCanceled,
        string? Place,
        string Url,
        long? StartTimestamp);

    public record FacebookEventDetail(
        string? Description,
        long? EndTimestamp,
        string? TicketUrl);

    public IReadOnlyList<FacebookEventStub> ParseEventList(string html)
    {
        var anchors = EventAnchorPattern().Matches(html);
        var stubs = new List<FacebookEventStub>();
        var seen = new HashSet<string>();

        foreach (Match anchor in anchors)
        {
            var id = anchor.Groups[1].Value;
            if (!seen.Add(id)) continue;

            try
            {
                var json = ExtractBalancedObject(html, anchor.Index);
                if (json is null) continue;
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var isCanceled = root.TryGetProperty("is_canceled", out var c) && c.ValueKind == JsonValueKind.True;
                var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
                string? place = null;
                if (root.TryGetProperty("event_place", out var p) && p.ValueKind == JsonValueKind.Object
                    && p.TryGetProperty("contextual_name", out var cn))
                    place = cn.GetString();

                // start_timestamp sits in a sibling renderer object, so look for it
                // in the slice between this event node and the next different one.
                var sliceEnd = anchors.Cast<Match>()
                    .Where(m => m.Index > anchor.Index && m.Groups[1].Value != id)
                    .Select(m => m.Index)
                    .DefaultIfEmpty(html.Length)
                    .First();
                var tsMatch = StartTimestampPattern().Match(html, anchor.Index, sliceEnd - anchor.Index);

                stubs.Add(new FacebookEventStub(
                    Id: id,
                    Name: name,
                    IsCanceled: isCanceled,
                    Place: place,
                    Url: url ?? $"https://www.facebook.com/events/{id}/",
                    StartTimestamp: tsMatch.Success ? long.Parse(tsMatch.Groups[1].Value) : null));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse Facebook event node {Id}", id);
            }
        }

        return stubs;
    }

    public FacebookEventDetail ParseEventDetail(string html)
    {
        var description = ExtractJsonString(html, "\"event_description\":{\"text\":\"");
        var ticketUrl = ExtractJsonString(html, "\"event_buy_ticket_url\":\"");

        long? endTs = null;
        var endMatch = EndTimestampPattern().Match(html);
        if (endMatch.Success && long.TryParse(endMatch.Groups[1].Value, out var ts) && ts > 0)
            endTs = ts;

        return new FacebookEventDetail(description, endTs, ticketUrl);
    }

    public static IReadOnlyList<string> ExtractUrls(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : UrlPattern().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')')).Distinct().ToList();

    /// <summary>Returns the balanced {...} JSON object starting at <paramref name="start"/>.</summary>
    private static string? ExtractBalancedObject(string src, int start)
    {
        var depth = 0;
        var inString = false;
        for (var i = start; i < src.Length; i++)
        {
            var ch = src[i];
            if (inString)
            {
                if (ch == '\\') i++;
                else if (ch == '"') inString = false;
                continue;
            }
            switch (ch)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return src[start..(i + 1)];
                    break;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the JSON string literal that follows <paramref name="anchor"/> and
    /// returns its unescaped value.
    /// </summary>
    private static string? ExtractJsonString(string src, string anchor)
    {
        var i = src.IndexOf(anchor, StringComparison.Ordinal);
        if (i < 0) return null;
        var start = i + anchor.Length; // first char after the opening quote

        var j = start;
        while (j < src.Length)
        {
            if (src[j] == '\\') j += 2;
            else if (src[j] == '"') break;
            else j++;
        }
        if (j >= src.Length) return null;

        try
        {
            return JsonSerializer.Deserialize<string>($"\"{src[start..j]}\"");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\{""__typename"":""Event"",""id"":""(\d+)""")]
    private static partial Regex EventAnchorPattern();

    [GeneratedRegex(@"""start_timestamp"":(\d+)")]
    private static partial Regex StartTimestampPattern();

    [GeneratedRegex(@"""end_timestamp"":(\d+)")]
    private static partial Regex EndTimestampPattern();

    [GeneratedRegex(@"https?://[^\s<>""\\]+")]
    private static partial Regex UrlPattern();
}
