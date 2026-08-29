using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace EnputMethod.Overlay;

internal static class EmojiAssetResolver
{
    private const char Separator = '\x1F';
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.Ordinal);

    internal static string EmojiFromCandidate(string candidate)
    {
        int separator = candidate.IndexOf(Separator);
        return separator < 0 ? candidate : candidate[..separator];
    }

    internal static string LabelFromCandidate(string candidate)
    {
        int separator = candidate.IndexOf(Separator);
        return separator < 0 ? candidate : candidate[(separator + 1)..];
    }

    internal static string FileNameFor(string emoji)
    {
        var scalarValues = new List<string>();
        foreach (Rune rune in emoji.EnumerateRunes())
        {
            if (rune.Value != 0xFE0F) scalarValues.Add(rune.Value.ToString("x"));
        }
        return string.Join('-', scalarValues) + ".png";
    }

    internal static BitmapImage? Load(string emoji)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "EmojiAssets", FileNameFor(emoji));
        if (Cache.TryGetValue(path, out BitmapImage? cached)) return cached;
        if (!File.Exists(path)) return Cache[path] = null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return Cache[path] = image;
    }
}