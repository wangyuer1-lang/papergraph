using System.Runtime.InteropServices;

namespace Papergraph;

// Avalonia 11 reports mouse wheels and trackpad scrolling as the same event.
// Read AppKit's gesture phases while that event is being dispatched; do not
// guess from delta size (high-resolution mouse wheels also send small deltas).
internal static class MacScrollInput
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nuint Integer(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern double Number(IntPtr receiver, IntPtr selector);

    internal static bool IsGesture(ulong timestamp)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
        var current = Send(app, sel_registerName("currentEvent"));
        if (current == IntPtr.Zero || Integer(current, sel_registerName("type")) != 22) return false; // NSEventTypeScrollWheel
        return MatchesGesture(timestamp, Number(current, sel_registerName("timestamp")),
            Integer(current, sel_registerName("phase")), Integer(current, sel_registerName("momentumPhase")));
    }

    internal static bool MatchesGesture(ulong timestamp, double nativeSeconds, nuint phase, nuint momentumPhase)
    {
        // AvnView.mm converts the native event timestamp to integer milliseconds.
        // Ignore stale currentEvent metadata when handling a synthetic/delayed event.
        var milliseconds = nativeSeconds * 1000;
        return double.IsFinite(milliseconds) && milliseconds >= 0 && milliseconds < ulong.MaxValue
            && (ulong)milliseconds == timestamp && (phase != 0 || momentumPhase != 0);
    }
}
