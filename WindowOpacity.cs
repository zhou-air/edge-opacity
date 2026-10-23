using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace EdgeOpacity;

public sealed record BrowserWindow(nint Handle, uint ProcessId, long StartedAt, string Title)
{
    public static BrowserWindow? Read(nint hwnd)
    {
        if (!Native.IsWindow(hwnd)) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var title = new StringBuilder(1024);
            Native.GetWindowText(hwnd, title, title.Capacity);
            return new(hwnd, pid, process.StartTime.ToUniversalTime().Ticks, title.ToString());
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        { return null; }
    }

    public bool IsSameWindow()
    {
        var current = Read(Handle);
        return current is not null && current.ProcessId == ProcessId && current.StartedAt == StartedAt;
    }
}

public static class EdgeWindows
{
    public static IReadOnlyList<BrowserWindow> List()
    {
        var result = new List<BrowserWindow>();
        var processes = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("msedge"))
        {
            using (process) processes.Add((uint)process.Id);
        }
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.GetWindow(hwnd, 4) != 0) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (!processes.Contains(pid)) return true;
            var className = new StringBuilder(128);
            Native.GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() != "Chrome_WidgetWin_1") return true;
            var window = BrowserWindow.Read(hwnd);
            if (window is not null && !string.IsNullOrWhiteSpace(window.Title)) result.Add(window);
            return true;
        }, 0);
        return result.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}

public sealed class WindowOpacity
{
    private sealed record Original(BrowserWindow Window, bool Layered, uint ColorKey, byte Alpha, uint Flags);
    private readonly Dictionary<nint, Original> _originals = new();
    public int ChangedCount { get { RemoveClosed(); return _originals.Count; } }

    public int ReadPercent(BrowserWindow window)
    {
        if (!window.IsSameWindow()) throw new InvalidOperationException("这个窗口已关闭，请刷新列表。");
        if (Native.GetLayeredWindowAttributes(window.Handle, out _, out byte alpha, out uint flags)
            && (flags & Native.LwaAlpha) != 0)
            return (int)Math.Round(alpha * 100.0 / 255);
        return 100;
    }

    public void Apply(BrowserWindow window, int percent)
    {
        if (percent is < 5 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (!window.IsSameWindow()) throw new InvalidOperationException("这个窗口已关闭，请刷新列表。");
        RemoveClosed();
        if (!_originals.TryGetValue(window.Handle, out var original))
        {
            bool layered = (Native.GetStyle(window.Handle) & Native.WsExLayered) != 0;
            uint key = 0, flags = 0;
            byte alpha = 255;
            if (layered && !Native.GetLayeredWindowAttributes(window.Handle, out key, out alpha, out flags))
                throw new InvalidOperationException("该窗口采用特殊绘制方式，无法安全调整透明度。");
            original = new(window, layered, key, alpha, flags);
            _originals.Add(window.Handle, original);
        }
        try
        {
            var style = Native.GetStyle(window.Handle);
            if ((style & Native.WsExLayered) == 0) Native.SetStyle(window.Handle, style | Native.WsExLayered);
            byte value = (byte)Math.Round(percent * 255.0 / 100);
            uint flags = Native.LwaAlpha | (original.Flags & Native.LwaColorKey);
            Native.Ensure(Native.SetLayeredWindowAttributes(window.Handle, original.ColorKey, value, flags));
        }
        catch
        {
            try { Restore(window); } catch { /* Keep the snapshot for a later recovery attempt. */ }
            throw;
        }
    }

    public void Restore(BrowserWindow window)
    {
        if (!_originals.TryGetValue(window.Handle, out var original)) return;
        if (original.Window.IsSameWindow())
        {
            if (original.Layered)
            {
                Native.SetStyle(window.Handle, Native.GetStyle(window.Handle) | Native.WsExLayered);
                Native.Ensure(Native.SetLayeredWindowAttributes(window.Handle, original.ColorKey, original.Alpha, original.Flags));
            }
            else
            {
                Native.SetStyle(window.Handle, Native.GetStyle(window.Handle) & ~Native.WsExLayered);
                Native.RedrawWindow(window.Handle, 0, 0, 0x485);
            }
        }
        _originals.Remove(window.Handle);
    }

    public IReadOnlyList<string> RestoreAll()
    {
        var failures = new List<string>();
        foreach (var original in _originals.Values.ToArray())
        {
            try { Restore(original.Window); }
            catch (Exception e) when (e is Win32Exception or InvalidOperationException)
            { failures.Add($"{original.Window.Title}：{Describe(e)}"); }
        }
        return failures;
    }

    private void RemoveClosed()
    {
        foreach (var pair in _originals.ToArray())
            if (!pair.Value.Window.IsSameWindow()) _originals.Remove(pair.Key);
    }

    public static string Describe(Exception error) => error is Win32Exception native && native.NativeErrorCode == 5
        ? "权限不足。请让 Edge 和本程序以相同权限运行。" : error.Message;
}

internal static class Native
{
    public const long WsExLayered = 0x80000;
    public const uint LwaColorKey = 1, LwaAlpha = 2;
    public delegate bool EnumWindowsCallback(nint hwnd, nint lParam);
    public delegate nint SubclassCallback(nint hwnd, uint msg, nuint wParam, nint lParam, nuint id, nuint data);

    public static void Ensure(bool succeeded)
    {
        if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static long GetStyle(nint hwnd)
    {
        Marshal.SetLastPInvokeError(0);
        var value = GetWindowLongPtr(hwnd, -20);
        if (value == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return value.ToInt64();
    }
    public static void SetStyle(nint hwnd, long value)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(hwnd, -20, (nint)value);
        if (previous == 0 && Marshal.GetLastWin32Error() != 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsCallback callback, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] public static extern bool RedrawWindow(nint hwnd, nint updateRect, nint updateRegion, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("comctl32.dll", SetLastError = true)] public static extern bool SetWindowSubclass(nint hwnd, SubclassCallback callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint hwnd, SubclassCallback callback, nuint id);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string? className, string title);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
}
