using System.Globalization;
using Captio.Core.Library;

namespace Captio.Core.Tests;

public sealed class CaptureFileNameTests : IDisposable
{
    private static readonly DateTime At = new(2026, 10, 30, 1, 33, 40);
    private readonly string _root = TempDir.Create();

    public void Dispose() => TempDir.Delete(_root);

    [Theory]
    [InlineData("es-ES", "Captura de pantalla 2026-10-30 013340")]
    [InlineData("es-AR", "Captura de pantalla 2026-10-30 013340")]
    [InlineData("en-US", "Screenshot 2026-10-30 013340")]
    [InlineData("en-GB", "Screenshot 2026-10-30 013340")]
    [InlineData("pt-BR", "Captura de tela 2026-10-30 013340")]
    [InlineData("pt-PT", "Captura de ecrã 2026-10-30 013340")]
    [InlineData("fr-FR", "Capture d'écran 2026-10-30 013340")]
    [InlineData("de-DE", "Screenshot 2026-10-30 013340")]
    [InlineData("ja-JP", "スクリーンショット 2026-10-30 013340")]
    [InlineData("zh-CN", "屏幕截图 2026-10-30 013340")]
    [InlineData("zh-TW", "螢幕擷取畫面 2026-10-30 013340")]
    public void Names_follow_the_display_language(string culture, string expected)
    {
        Assert.Equal(expected, CaptureFileName.For(At, CultureInfo.GetCultureInfo(culture)));
    }

    [Theory]
    [InlineData("sw-KE")] // a language without an entry
    [InlineData("")]      // invariant
    public void Unknown_languages_fall_back_to_english(string culture)
    {
        Assert.Equal("Screenshot 2026-10-30 013340", CaptureFileName.For(At, CultureInfo.GetCultureInfo(culture)));
    }

    [Fact]
    public void Date_part_ignores_regional_formats_and_uses_24_hours()
    {
        var afternoon = new DateTime(2026, 1, 5, 17, 4, 9);

        // Arabic regions use another calendar and digits; US uses 12-hour time. Neither may leak into the name.
        Assert.EndsWith(" 2026-01-05 170409", CaptureFileName.For(afternoon, CultureInfo.GetCultureInfo("ar-SA")));
        Assert.EndsWith(" 2026-01-05 170409", CaptureFileName.For(afternoon, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Every_name_is_a_valid_file_name()
    {
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var name = CaptureFileName.For(At, culture);
            Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, $"{culture.Name}: {name}");
        }
    }

    [Fact]
    public void Saved_captures_use_the_name_and_number_repeats_in_the_same_second()
    {
        var store = new LibraryStore(_root);
        var spanish = CultureInfo.GetCultureInfo("es-ES");
        var when = new DateTimeOffset(At, TimeZoneInfo.Local.GetUtcOffset(At));

        var first = store.AddImage(TestPng.Create(1, 1), when, spanish);
        var second = store.AddImage(TestPng.Create(1, 1), when, spanish);

        Assert.Equal("Captura de pantalla 2026-10-30 013340.png", first.FileName);
        Assert.Equal("Captura de pantalla 2026-10-30 013340 (2).png", second.FileName);
    }
}
