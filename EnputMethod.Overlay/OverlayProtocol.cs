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
    public string? Surface { get; init; }
    public int? CandidateIndex { get; init; }
    public CandidateView? Candidates { get; init; }
    public TranslationView? Translation { get; init; }

    [JsonIgnore]
    public bool IsValid => Type switch
    {
        "showCandidates" => HasClientId && StateId > 0 && Candidates is not null && Candidates.IsValid,
        "showTranslation" => HasClientId && StateId > 0 && Translation is not null && Translation.IsValid,
        "hide" => HasClientId && StateId > 0 && (Surface is null or "all" or "candidates" or "translation"),
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
    public int CompositionLeft { get; init; }
    public int CompositionTop { get; init; }
    public int CompositionRight { get; init; }
    public int CompositionBottom { get; init; }
    public long OwnerWindow { get; init; }
    public IReadOnlyList<CandidateItemView> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageCount { get; init; }
    public int SelectedIndex { get; init; }
    public bool CapsLock { get; init; }
    public string Layout { get; init; } = "vertical";
    public string? ModeMarker { get; init; }
    public OverlayTheme? Theme { get; init; }

    [JsonIgnore]
    public bool HasCompositionBounds => CompositionRight > CompositionLeft && CompositionBottom > CompositionTop;

    [JsonIgnore]
    public bool IsValid => Items.Count > 0 && Page >= 0 && Page < PageCount && SelectedIndex >= 0 && SelectedIndex < Items.Count && Layout is "vertical" or "horizontal";
}

internal sealed record CandidateItemView
{
    public string Text { get; init; } = "";
    public bool CanonicalCaseRequired { get; init; }
}

internal sealed record TranslationView
{
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public int CandidateRight { get; init; }
    public int CandidateTop { get; init; }
    public long OwnerWindow { get; init; }
    public OverlayTheme? Theme { get; init; }

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Title);
}

internal sealed record OverlayTheme
{
    public string Background { get; init; } = "#1f262e";
    public string Foreground { get; init; } = "#ffffff";
    public string Border { get; init; } = "#616f7e";
    public string SelectedBackground { get; init; } = "#2c597a";
    public string SelectedForeground { get; init; } = "#ffffff";
    public string TranslationBackground { get; init; } = "#1f262e";
    public string TranslationForeground { get; init; } = "#f5f5f5";
    public string TranslationTitleForeground { get; init; } = "#ffffff";
    public string TranslationPartForeground { get; init; } = "#93c5fd";
    public string TranslationLabelForeground { get; init; } = "#a5f3fc";
    public string TranslationExampleForeground { get; init; } = "#c4b5fd";
    public string TranslationExampleBackground { get; init; } = "#312e4b";
    public string TranslationBorder { get; init; } = "#616f7e";
    public string TranslationScrollbarTrack { get; init; } = "#374151";
    public string TranslationScrollbarThumb { get; init; } = "#60a5fa";
    public string FontFamily { get; init; } = "Segoe UI";
    public int FontSize { get; init; } = 18;
    public int Opacity { get; init; } = 255;
    public int BorderWidth { get; init; } = 1;
    public int CornerRadius { get; init; } = 4;
    public int Padding { get; init; } = 6;
    public int RowHeight { get; init; } = 28;
    public int TranslationBorderWidth { get; init; } = 1;
    public int TranslationCornerRadius { get; init; } = 4;
    public int TranslationPadding { get; init; } = 8;
    public int TranslationWidth { get; init; } = 280;
    public int TranslationMaxHeight { get; init; } = 180;
    public int TranslationWindowWidth { get; init; } = 380;
    public int TranslationWindowHeight { get; init; } = 280;

    // The native configuration expresses point sizes; WPF measures font sizes in 96-DPI pixels.
    [JsonIgnore]
    public double WpfFontSize => FontSize * 96.0 / 72.0;

    [JsonIgnore]
    public bool IsValid => FontSize is >= 8 and <= 72 && Opacity is >= 32 and <= 255 &&
        BorderWidth is >= 0 and <= 8 && CornerRadius is >= 0 and <= 32 && Padding is >= 0 and <= 48 &&
        RowHeight is >= 16 and <= 96 && TranslationWidth is >= 160 and <= 1000 && TranslationMaxHeight is >= 80 and <= 1200 &&
        TranslationWindowWidth is >= 260 and <= 1200 && TranslationWindowHeight is >= 160 and <= 900;
}
