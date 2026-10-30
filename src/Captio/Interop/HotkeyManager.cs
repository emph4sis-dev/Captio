using System.Windows.Interop;
using Captio.Core.Settings;

namespace Captio.Interop;

/// <summary>Registers system-wide hotkeys on a message-only window.</summary>
internal sealed class HotkeyManager : IDisposable
{
    private static readonly nint HWND_MESSAGE = -3;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("CaptioHotkeys") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <returns>false when another application or the system already owns the combination.</returns>
    public bool TryRegister(Hotkey hotkey, Action handler)
    {
        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, hotkey.Modifiers | Native.MOD_NOREPEAT, hotkey.VirtualKey)) return false;
        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var handler))
        {
            handled = true;
            handler();
        }
        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
