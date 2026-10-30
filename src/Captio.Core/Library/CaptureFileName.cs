using System.Globalization;

namespace Captio.Core.Library;

/// <summary>
/// Names saved captures the way Windows names its own: the word for "screenshot" in the
/// display language, then the local date and time as yyyy-MM-dd HHmmss
/// ("Captura de pantalla 2026-10-30 013340", "Screenshot 2026-10-30 013340").
/// The date part never changes with the regional format, so files sort by name in time order.
/// </summary>
public static class CaptureFileName
{
    public const string Fallback = "Screenshot";

    // Windows' own term per language. Region-specific entries win over the bare language.
    private static readonly Dictionary<string, string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "Screenshot",
        ["es"] = "Captura de pantalla",
        ["ca"] = "Captura de pantalla",
        ["gl"] = "Captura de pantalla",
        ["pt"] = "Captura de tela",
        ["pt-PT"] = "Captura de ecrã",
        ["fr"] = "Capture d'écran",
        ["it"] = "Screenshot",
        ["de"] = "Screenshot",
        ["nl"] = "Schermafbeelding",
        ["sv"] = "Skärmbild",
        ["da"] = "Skærmbillede",
        ["nb"] = "Skjermbilde",
        ["nn"] = "Skjermbilde",
        ["no"] = "Skjermbilde",
        ["fi"] = "Näyttökuva",
        ["pl"] = "Zrzut ekranu",
        ["cs"] = "Snímek obrazovky",
        ["sk"] = "Snímka obrazovky",
        ["hu"] = "Képernyőkép",
        ["ro"] = "Captură de ecran",
        ["tr"] = "Ekran görüntüsü",
        ["el"] = "Στιγμιότυπο οθόνης",
        ["ru"] = "Снимок экрана",
        ["uk"] = "Знімок екрана",
        ["ja"] = "スクリーンショット",
        ["ko"] = "스크린샷",
        ["zh-Hans"] = "屏幕截图",
        ["zh-Hant"] = "螢幕擷取畫面",
    };

    /// <summary>The name without extension, for a local time and a display language.</summary>
    public static string For(DateTime localTime, CultureInfo displayLanguage) =>
        $"{WordFor(displayLanguage)} {localTime.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Walks from the most specific culture to the neutral one ("pt-PT" → "pt"; "zh-CN" → "zh-Hans")
    /// and falls back to English for languages without an entry.
    /// </summary>
    public static string WordFor(CultureInfo culture)
    {
        for (var c = culture; !Equals(c, CultureInfo.InvariantCulture); c = c.Parent)
        {
            if (Words.TryGetValue(c.Name, out var word)) return word;
        }
        return Fallback;
    }
}
