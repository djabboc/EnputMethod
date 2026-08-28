using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnputMethod.Overlay;

internal static class OverlayProtocol
{
    internal const string PipeName = "EnputMethod.Overlay.v1";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static bool TryParse(string line, out OverlayMessage? message)
    {
        message = null;
        try
        {
            message = JsonSerializer.Deserialize<OverlayMessage>(line, JsonOptions);
            return message is not null && message.IsValid;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

internal sealed record OverlayMessage
{
    public string Type { get; init; } = "";
    public string ClientId { get; init; } = "";
    public long StateId { get; init; }
    public int? CandidateIndex { get; init; }
    public CandidateView? Candidates { get; init; }
    public TranslationView? Translation { get; init; }

    [JsonIgnore]
    public bool IsValid => Type switch
    {
        "showCandidates" => HasClientId && StateId > 0 && Candidates is not null && Candidates.IsValid,
        "showTranslation" => HasClientId && StateId > 0 && Translation is not null && Translation.IsValid,
        "hide" => HasClientId && StateId > 0,
        "selectCandidate" => HasClientId && StateId > 0 && CandidateIndex is >= 0,
        "previousPage" or "nextPage" or "dismiss" => HasClientId && StateId > 0,
        _ => false,
    };

    private bool HasClientId => !string.IsNullOrWhiteSpace(ClientId) && ClientId.Length <= 128;
}

internal sealed record CandidateView
{
    public int X { get; init; }
    public int Y { get; init; }
    public IReadOnlyList<string> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageCount { get; init; }
    public int SelectedIndex { get; init; }
    public string Layout { get; init; } = "vertical";
    public string? ModeMarker { get; init; }

    [JsonIgnore]
    public bool IsValid => Items.Count > 0 && Page >= 0 && Page < PageCount && SelectedIndex >= 0 && SelectedIndex < Items.Count && Layout is "vertical" or "horizontal";
}

internal sealed record TranslationView
{
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public int CandidateRight { get; init; }
    public int CandidateTop { get; init; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Title);
}
