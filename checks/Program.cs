using System.Runtime.InteropServices;
using EdgeOpacity;

int assertions = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + label);
    assertions++;
    Console.WriteLine("PASS: " + label);
}

var windows = new List<nint>();
var controller = new WindowOpacity();
BrowserWindow Create()
{
    nint hwnd = TestNative.CreateWindowEx(0, "STATIC", "Opacity functional fixture", 0x00CF0000, 0, 0, 300, 200, 0, 0, 0, 0);
    Native.Ensure(hwnd != 0);
    windows.Add(hwnd);
    return BrowserWindow.Read(hwnd)!;
}

try
{
    var plain = Create();
    long originalStyle = Native.GetStyle(plain.Handle);
    Check(controller.ReadPercent(plain) == 100, "unmodified window is opaque");
    controller.Apply(plain, 5);
    Check(Native.GetLayeredWindowAttributes(plain.Handle, out _, out byte alpha, out uint flags) && alpha == 13 && flags == 2,
        "5 percent applies alpha 13 without click-through");
    Check(controller.ReadPercent(plain) == 5, "readback returns 5 percent");
    controller.Apply(plain, 60);
    Check(Native.GetLayeredWindowAttributes(plain.Handle, out _, out alpha, out _) && alpha == 153, "60 percent preset alpha");
    controller.Apply(plain, 100);
    Check(controller.ReadPercent(plain) == 100, "100 percent readback");
    // Restoration changes only the layer bit, preserving unrelated changes made in the meantime.
    Native.SetStyle(plain.Handle, Native.GetStyle(plain.Handle) | 0x80);
    controller.Restore(plain);
    Check(Native.GetStyle(plain.Handle) == (originalStyle | 0x80), "original non-layered state restored, unrelated style preserved");
    Check(controller.ChangedCount == 0, "restored window no longer tracked");

    var layered = Create();
    Native.SetStyle(layered.Handle, Native.GetStyle(layered.Handle) | Native.WsExLayered);
    Native.Ensure(Native.SetLayeredWindowAttributes(layered.Handle, 0x123456, 77, 3));
    controller.Apply(layered, 20);
    Check(Native.GetLayeredWindowAttributes(layered.Handle, out uint key, out alpha, out flags) && key == 0x123456 && alpha == 51 && flags == 3,
        "existing color key preserved while adjusting alpha");
    controller.Apply(layered, 80);
    controller.Restore(layered);
    Check(Native.GetLayeredWindowAttributes(layered.Handle, out key, out alpha, out flags) && key == 0x123456 && alpha == 77 && flags == 3,
        "repeated adjustment restores original alpha, key and flags");

    foreach (int invalid in new[] { 4, 101 })
    {
        bool rejected = false;
        try { controller.Apply(plain, invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected && controller.ChangedCount == 0, $"invalid value {invalid} rejected without mutation");
    }

    controller.Apply(plain, 5);
    controller.Apply(layered, 5);
    Check(controller.ChangedCount == 2, "multiple windows tracked independently");
    Check(controller.RestoreAll().Count == 0 && controller.ChangedCount == 0, "restore all succeeds");
    Check((Native.GetStyle(plain.Handle) & Native.WsExLayered) == 0, "restore all clears introduced layering");
    Check(Native.GetLayeredWindowAttributes(layered.Handle, out _, out alpha, out _) && alpha == 77, "restore all preserves pre-existing opacity");

    var closed = Create();
    controller.Apply(closed, 5);
    TestNative.DestroyWindow(closed.Handle);
    windows.Remove(closed.Handle);
    Check(controller.RestoreAll().Count == 0 && controller.ChangedCount == 0, "closed target safely discarded");
    bool staleRejected = false;
    try { controller.Apply(closed, 50); } catch (InvalidOperationException) { staleRejected = true; }
    Check(staleRejected, "closed target cannot be adjusted");
    Check(EdgeWindows.List().All(w => w.ProcessId != Environment.ProcessId), "window list excludes non-Edge processes");
    Console.WriteLine($"SUCCESS: {assertions} assertions");
}
finally
{
    controller.RestoreAll();
    foreach (nint hwnd in windows) TestNative.DestroyWindow(hwnd);
}

internal static class TestNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
}
