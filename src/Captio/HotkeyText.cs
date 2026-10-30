using System.Windows.Input;
using Captio.Core.Settings;

namespace Captio;

internal static class HotkeyText
{
    public static string Format(Hotkey hotkey)
    {
        var parts = new List<string>();
        if ((hotkey.Modifiers & Hotkey.Control) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & Hotkey.Alt) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & Hotkey.Shift) != 0) parts.Add("Mayús");
        if ((hotkey.Modifiers & Hotkey.Win) != 0) parts.Add("Win");

        var key = KeyInterop.KeyFromVirtualKey((int)hotkey.VirtualKey);
        parts.Add(key == Key.Snapshot ? "ImpPant" : key.ToString());
        return string.Join("+", parts);
    }
}
