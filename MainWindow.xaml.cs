using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace EdgeOpacity;

public sealed partial class MainWindow : Window
{
    private readonly WindowOpacity _opacity = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Native.SubclassCallback _hotkeyCallback;
    private readonly nint _hwnd;
    private bool _syncing;
    private bool _ready;
    private bool _hotkeyRegistered;
    private const int RestoreHotkey = 0xED6;
    private BrowserWindow? Selected => WindowPicker.SelectedItem as BrowserWindow;
    private ToggleButton[] Presets => [Preset100, Preset80, Preset60, Preset40, Preset20, Preset5];

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(650 * dpi), (int)(700 * dpi)));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(560 * dpi);
            presenter.PreferredMinimumHeight = (int)(500 * dpi);
        }
        _hotkeyCallback = WindowMessage;
        if (Native.SetWindowSubclass(_hwnd, _hotkeyCallback, 1, 0))
            _hotkeyRegistered = Native.RegisterHotKey(_hwnd, RestoreHotkey, 0x4003, 0x30);
        if (!_hotkeyRegistered) HotkeyHint.Text = "Ctrl + Alt + 0 未能注册（可能已被占用），可使用“恢复全部窗口”。";
        _ready = true;
        RefreshWindows();
        _refreshTimer.Tick += (_, _) => RefreshWindows();
        _refreshTimer.Start();
        AppWindow.Closing += OnClosing;
    }

    private void RefreshWindows()
    {
        var selected = Selected;
        try
        {
            var windows = EdgeWindows.List();
            var current = WindowPicker.ItemsSource as IReadOnlyList<BrowserWindow>;
            if (current is null || !current.SequenceEqual(windows))
            {
                _syncing = true;
                try
                {
                    WindowPicker.ItemsSource = windows;
                    WindowPicker.SelectedItem = windows.FirstOrDefault(w => selected is not null
                        && w.Handle == selected.Handle && w.ProcessId == selected.ProcessId && w.StartedAt == selected.StartedAt)
                        ?? windows.FirstOrDefault();
                }
                finally { _syncing = false; }
            }
            WindowHint.Text = windows.Count == 0 ? "未找到 Edge 窗口。打开 Edge 后会自动显示。"
                : $"共 {windows.Count} 个窗口，调整仅作用于选中的窗口。";
            SyncControls();
        }
        catch (Win32Exception e) { SetStatus(WindowOpacity.Describe(e), true); }
    }

    private void SyncControls()
    {
        bool available = Selected is not null;
        OpacitySlider.IsEnabled = available;
        PresetsControl.IsEnabled = available;
        RestoreSelected.IsEnabled = available;
        RestoreAll.IsEnabled = _opacity.ChangedCount > 0;
        _syncing = true;
        try
        {
            int percent = Selected is { } window ? _opacity.ReadPercent(window) : 100;
            OpacitySlider.Value = Math.Clamp(percent, 5, 100);
            PercentLabel.Text = $"{percent}%";
            foreach (var preset in Presets) preset.IsChecked = int.Parse((string)preset.Tag) == percent;
        }
        catch (InvalidOperationException e) { SetStatus(e.Message, true); }
        finally { _syncing = false; }
    }

    private void SetStatus(string message, bool error = false)
    {
        Status.Severity = error ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        Status.Message = message;
        Status.IsOpen = true;
    }

    private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_ready || _syncing || Selected is not { } window) return;
        try
        {
            int percent = (int)Math.Round(e.NewValue);
            _opacity.Apply(window, percent);
            PercentLabel.Text = $"{percent}%";
            foreach (var preset in Presets) preset.IsChecked = int.Parse((string)preset.Tag) == percent;
            RestoreAll.IsEnabled = _opacity.ChangedCount > 0;
            SetStatus($"已设为 {percent}%。" + (percent <= 15 ? "窗口较淡，可按 Ctrl + Alt + 0 恢复。" : ""));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            SyncControls();
            SetStatus(WindowOpacity.Describe(error), true);
        }
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        OpacitySlider.Value = int.Parse((string)((ToggleButton)sender).Tag);
        SyncControls();
    }
    private void WindowPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_syncing) SyncControls(); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshWindows();
    private void RestoreSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } window) return;
        try
        {
            _opacity.Restore(window);
            SyncControls();
            SetStatus("当前窗口已恢复原来的透明度。");
        }
        catch (Win32Exception error) { SetStatus(WindowOpacity.Describe(error), true); }
    }
    private void RestoreAll_Click(object sender, RoutedEventArgs e) => RestoreEverything();
    public void RestoreOnFailure() => _opacity.RestoreAll();
    private bool RestoreEverything()
    {
        var failures = _opacity.RestoreAll();
        SyncControls();
        SetStatus(failures.Count == 0 ? "全部窗口已恢复原来的透明度。" : string.Join("\n", failures), failures.Count > 0);
        return failures.Count == 0;
    }
    private nint WindowMessage(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (msg == 0x0312 && (int)wParam == RestoreHotkey)
        {
            // Queue UI work rather than allowing a managed exception across the native callback.
            DispatcherQueue.TryEnqueue(() => RestoreEverything());
            return 0;
        }
        return Native.DefSubclassProc(hwnd, msg, wParam, lParam);
    }
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!RestoreEverything())
        {
            args.Cancel = true;
            SetStatus("部分窗口未能恢复。请先关闭相关 Edge 窗口，再关闭本程序。", true);
            return;
        }
        _refreshTimer.Stop();
        if (_hotkeyRegistered) Native.UnregisterHotKey(_hwnd, RestoreHotkey);
        Native.RemoveWindowSubclass(_hwnd, _hotkeyCallback, 1);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
}
