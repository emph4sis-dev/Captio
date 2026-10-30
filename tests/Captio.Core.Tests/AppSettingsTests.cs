using Captio.Core.Settings;

namespace Captio.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Imgur_default_requires_consent()
    {
        var settings = new AppSettings { DefaultDestination = Destination.Imgur };
        Assert.Equal(Destination.Copy, settings.EffectiveDefaultDestination);

        Assert.Equal(Destination.Imgur, (settings with { ImgurConsentAccepted = true }).EffectiveDefaultDestination);
    }

    [Fact]
    public void Settings_round_trip_and_survive_corruption()
    {
        var path = Path.Combine(Path.GetTempPath(), "captio-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new SettingsStore(path);
            var saved = new AppSettings { DefaultDestination = Destination.Save, ArrowThickness = 9 };
            store.Save(saved);
            Assert.Equal(saved, store.Load());

            File.WriteAllText(path, "nope");
            Assert.Equal(new AppSettings(), store.Load());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
