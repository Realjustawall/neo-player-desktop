using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NeoPlayer.Windows.Models;
using NeoPlayer.Windows.ViewModels;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace NeoPlayer.Windows.Services;

public sealed class WindowsIntegrationService : IDisposable
{
    readonly PlaybackEngine playback;
    readonly ArtworkService artwork;
    NotifyIcon? tray;
    Window? window;
    NeoPlayer.Windows.Views.CompactPlayerWindow? compact;
    MainViewModel? viewModel;
    HwndSource? source;
    MMDeviceEnumerator? devices;
    DeviceNotification? notification;
    Windows.Media.Playback.MediaPlayer? mediaPlayer;
    Windows.Media.SystemMediaTransportControls? smtc;
    long lastMetadataSong = long.MinValue;
    long lastTimelineTick;

    public WindowsIntegrationService(PlaybackEngine p, ArtworkService a)
    {
        playback = p; artwork = a; p.StateChanged += PlaybackChanged;
    }

    public void Attach(Window w, MainViewModel vm)
    {
        window = w; viewModel = vm;
        w.SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(w).Handle; source = HwndSource.FromHwnd(hwnd); source?.AddHook(WndProc); };
        w.StateChanged += (_, _) => { if (w.WindowState == WindowState.Minimized && vm.Settings.MinimizeToTray) { w.Hide(); EnsureTray(vm); } };
        w.Closing += (_, e) => { if (vm.Settings.MinimizeToTray && !vm.ReallyExit) { e.Cancel = true; w.Hide(); EnsureTray(vm); } };
        SetupDevices(); SetupSmtc(); ApplyStartup(vm.Settings.StartWithWindows);
    }

    void EnsureTray(MainViewModel vm)
    {
        if (tray is not null) return;
        tray = new NotifyIcon { Text = "NEO Player", Visible = true, Icon = System.Drawing.SystemIcons.Application };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Show());
        menu.Items.Add("Play/Pause", null, (_, _) => playback.TogglePlay());
        menu.Items.Add("Next", null, async (_, _) => await playback.NextAsync());
        menu.Items.Add("Previous", null, async (_, _) => await playback.PreviousAsync());
        menu.Items.Add("Compact player", null, (_, _) => ShowCompact());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { vm.ReallyExit = true; tray!.Visible = false; System.Windows.Application.Current.Shutdown(); });
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Show();
    }

    public void ShowCompact()
    {
        if (viewModel is null) return;
        if (compact is null) compact = new NeoPlayer.Windows.Views.CompactPlayerWindow { DataContext = viewModel };
        compact.Show(); compact.Activate();
    }

    void Show()
    {
        if (window is null) return;
        window.Show(); if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled)
    {
        const int WM_APPCOMMAND = 0x0319;
        if (msg != WM_APPCOMMAND) return IntPtr.Zero;
        var cmd = ((int)((long)lp >> 16)) & 0xFFF; handled = true;
        switch (cmd)
        {
            case 14: playback.TogglePlay(); break;
            case 11: _ = playback.NextAsync(); break;
            case 12: _ = playback.PreviousAsync(); break;
            case 13: playback.Pause(); break;
            default: handled = false; break;
        }
        return IntPtr.Zero;
    }

    void SetupDevices()
    {
        try
        {
            devices = new MMDeviceEnumerator();
            var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
            notification = new DeviceNotification(devices, playback, current);
            devices.RegisterEndpointNotificationCallback(notification);
        }
        catch { }
    }

    void SetupSmtc()
    {
        try
        {
            mediaPlayer = new Windows.Media.Playback.MediaPlayer();
            mediaPlayer.CommandManager.IsEnabled = false;
            smtc = mediaPlayer.SystemMediaTransportControls;
            smtc.IsEnabled = true; smtc.IsPlayEnabled = smtc.IsPauseEnabled = smtc.IsNextEnabled = smtc.IsPreviousEnabled = smtc.IsStopEnabled = true;
            smtc.ButtonPressed += async (_, e) =>
            {
                switch (e.Button)
                {
                    case Windows.Media.SystemMediaTransportControlsButton.Play: playback.Resume(); break;
                    case Windows.Media.SystemMediaTransportControlsButton.Pause: playback.Pause(); break;
                    case Windows.Media.SystemMediaTransportControlsButton.Next: await playback.NextAsync(); break;
                    case Windows.Media.SystemMediaTransportControlsButton.Previous: await playback.PreviousAsync(); break;
                    case Windows.Media.SystemMediaTransportControlsButton.Stop: playback.Pause(); await playback.SeekAsync(0); break;
                }
            };
            smtc.PlaybackPositionChangeRequested += async (_, e) => await playback.SeekAsync((long)e.RequestedPlaybackPosition.TotalMilliseconds);
            smtc.PlaybackRateChangeRequested += async (_, e) => await playback.SetSpeedAsync((float)Math.Clamp(e.RequestedPlaybackRate, .25, 3));
        }
        catch { smtc = null; }
    }

    async void PlaybackChanged(object? s, EventArgs e)
    {
        if (smtc is null) return;
        try
        {
            smtc.PlaybackStatus = playback.State.Current is null ? Windows.Media.MediaPlaybackStatus.Closed : playback.State.IsPlaying ? Windows.Media.MediaPlaybackStatus.Playing : Windows.Media.MediaPlaybackStatus.Paused;
            smtc.PlaybackRate = playback.State.Speed;
            var song = playback.State.Current;
            if (song is not null && song.Id != lastMetadataSong)
            {
                lastMetadataSong = song.Id;
                var u = smtc.DisplayUpdater; u.Type = Windows.Media.MediaPlaybackType.Music;
                u.MusicProperties.Title = song.Title; u.MusicProperties.Artist = song.Artist; u.MusicProperties.AlbumTitle = song.Album;
                var art = await artwork.ResolveAsync(song);
                if (art is not null)
                    u.Thumbnail = Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(await Windows.Storage.StorageFile.GetFileFromPathAsync(art));
                u.Update();
            }
            var now = Environment.TickCount64;
            if (now - lastTimelineTick >= 750)
            {
                lastTimelineTick = now;
                var tl = new Windows.Media.SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    Position = TimeSpan.FromMilliseconds(playback.State.PositionMs),
                    EndTime = TimeSpan.FromMilliseconds(playback.State.DurationMs),
                    MinSeekTime = TimeSpan.Zero,
                    MaxSeekTime = TimeSpan.FromMilliseconds(playback.State.DurationMs)
                };
                smtc.UpdateTimelineProperties(tl);
            }
        }
        catch { }
    }

    public void ApplyStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable) key?.SetValue("NEO Player", $"\"{Environment.ProcessPath}\""); else key?.DeleteValue("NEO Player", false);
        }
        catch { }
    }

    public void Dispose()
    {
        playback.StateChanged -= PlaybackChanged;
        if (devices is not null && notification is not null) try { devices.UnregisterEndpointNotificationCallback(notification); } catch { }
        devices?.Dispose(); source?.RemoveHook(WndProc); tray?.Dispose();
        if (compact is not null) { compact.DataContext = null; compact = null; }
        mediaPlayer?.Dispose();
    }

    sealed class DeviceNotification : IMMNotificationClient
    {
        readonly MMDeviceEnumerator enumerator;
        readonly PlaybackEngine playback;
        string currentId;
        public DeviceNotification(MMDeviceEnumerator e, PlaybackEngine p, string current) { enumerator = e; playback = p; currentId = current; }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow != DataFlow.Render || role != Role.Multimedia) return;
            var shouldPause = false;
            try
            {
                var old = enumerator.GetDevice(currentId);
                shouldPause = old.State is DeviceState.NotPresent or DeviceState.Unplugged or DeviceState.Disabled;
            }
            catch { }
            currentId = defaultDeviceId;
            if (shouldPause) playback.Pause();
            _ = playback.ReinitializeOutputAsync();
        }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { if (deviceId == currentId) playback.Pause(); }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { if (deviceId == currentId && newState is DeviceState.NotPresent or DeviceState.Unplugged or DeviceState.Disabled) playback.Pause(); }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
