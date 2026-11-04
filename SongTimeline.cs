using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.SongPause;

public sealed record SongAtTime([property: JsonPropertyName("title")] string Title, [property: JsonPropertyName("artist")] string? Artist);

internal static class SongTimeline
{
    private sealed record Timeline(int Version, Cue?[]? Songs);
    private sealed record Cue(long? StartMs, long? EndMs, string? Title, string? Artist);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<List<SongAtTime>> At(Stream stream, long positionMs, CancellationToken cancellationToken)
    {
        var timeline = await JsonSerializer.DeserializeAsync<Timeline>(stream, JsonOptions, cancellationToken);
        if (timeline is not { Version: 1, Songs: not null })
            throw new InvalidDataException("Expected version 1 and a songs array.");

        var matches = new List<SongAtTime>();
        foreach (var cue in timeline.Songs)
        {
            if (cue?.StartMs is not long start || start < 0 || cue.EndMs is not long end || end <= start || string.IsNullOrWhiteSpace(cue.Title))
                throw new InvalidDataException("Invalid song interval or title.");
            if (start <= positionMs && positionMs < end)
                matches.Add(new SongAtTime(cue.Title.Trim(), cue.Artist?.Trim()));
        }
        return matches;
    }
}
