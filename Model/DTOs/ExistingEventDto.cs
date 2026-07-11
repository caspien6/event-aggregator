namespace Model.DTOs;

public record ExistingEventDto
{
    public required HashSet<string> Ids { get; init; }

    public required Dictionary<string, (string Id, string? ColorId)> ByUrlKey { get; init; }
}
