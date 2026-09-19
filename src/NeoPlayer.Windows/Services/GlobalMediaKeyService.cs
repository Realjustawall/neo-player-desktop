using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NeoPlayer.Windows.Services;

public sealed class GlobalMediaKeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaPlayPause = 0xB3;
    private const int PlayPauseId = 0x4E01;
    private const int NextId = 0x4E02;
    private const int PreviousId = 0x4E03;

    private HwndSource? _source;
    private IntPtr _handle;
    private bool _registered;

    public event EventHandler? PlayPausePressed;
    public event EventHandler? NextPressed;
    public event EventHandler? PreviousPressed;

    public void Attach(Window window)
    {
        if (_registered) return;
        _handle = new WindowInteropHelper(window).Handle;
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Window handle is not available yet.");
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("Unable to create HwndSource for media keys.");
        _source.AddHook(WndProc);
        var play = RegisterHotKey(_handle, PlayPauseId, 0, VkMediaPlayPause);
        var next = RegisterHotKey(_handle, NextId, 0, VkMediaNextTrack);
        var previous = RegisterHotKey(_handle, PreviousId, 0, VkMediaPrevTrack);
        _registered = play || next || previous;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey) return IntPtr.Zero;
        switch (wParam.ToInt32())
        {
            case PlayPauseId: PlayPausePressed?.Invoke(this, EventArgs.Empty); handled = true; break;
            case NextId: NextPressed?.Invoke(this, EventArgs.Empty); handled = true; break;
            case PreviousId: PreviousPressed?.Invoke(this, EventArgs.Empty); handled = true; break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, PlayPauseId);
            UnregisterHotKey(_handle, NextId);
            UnregisterHotKey(_handle, PreviousId);
        }
        _source?.RemoveHook(WndProc);
        _source = null;
        _handle = IntPtr.Zero;
        _registered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
