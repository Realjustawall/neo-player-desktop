using System.Globalization;
using System.Text.RegularExpressions;
using NeoPlayer.Windows.Models;

namespace NeoPlayer.Windows.Services;

public static partial class LyricsService
{
    [GeneratedRegex(@"\[(?<m>\d{1,2}):(?<s>\d{2})(?:[\.:](?<f>\d{1,3}))?\](?<t>.*)")]
    private static partial Regex LrcRegex();

    public static IReadOnlyList<LyricLine> ParseLrc(string text)
    {
        var list = new List<LyricLine>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var m = LrcRegex().Match(raw);
            if (!m.Success) continue;
            var min = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            var sec = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            var fracRaw = m.Groups["f"].Value;
            var ms = fracRaw.Length switch { 0 => 0, 1 => int.Parse(fracRaw) * 100, 2 => int.Parse(fracRaw) * 10, _ => int.Parse(fracRaw[..Math.Min(3, fracRaw.Length)]) };
            list.Add(new LyricLine(TimeSpan.FromMilliseconds((min * 60 + sec) * 1000 + ms), m.Groups["t"].Value.Trim()));
        }
        return list.OrderBy(x => x.Time).ToArray();
    }

    public static string? FindSidecar(string audioPath)
    {
        var basePath = Path.Combine(Path.GetDirectoryName(audioPath) ?? "", Path.GetFileNameWithoutExtension(audioPath));
        foreach (var ext in new[] { ".lrc", ".txt" }) if (File.Exists(basePath + ext)) return basePath + ext;
        return null;
    }
}
