using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Threading;

namespace Papergraph;

internal sealed class MacBluetooth : IBluetoothHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void NativeEvent(IntPtr json);
    [DllImport("libPapergraphBluetooth.dylib", CallingConvention = CallingConvention.Cdecl)] static extern void pg_bluetooth_start(int port, NativeEvent callback);
    [DllImport("libPapergraphBluetooth.dylib", CallingConvention = CallingConvention.Cdecl)] static extern void pg_bluetooth_stop();
    [DllImport("libPapergraphBluetooth.dylib", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr pg_bluetooth_qr([MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport("libPapergraphBluetooth.dylib", CallingConvention = CallingConvention.Cdecl)] static extern void pg_bluetooth_free(IntPtr text);
    internal static byte[]? Qr(string code)
    {
        var pointer = pg_bluetooth_qr(code);
        if (pointer == IntPtr.Zero) return null;
        try { return Convert.FromBase64String(Marshal.PtrToStringUTF8(pointer)!); }
        finally { pg_bluetooth_free(pointer); }
    }
    static readonly NativeEvent Callback = Receive;
    static MacBluetooth? current;
    readonly TaskCompletionSource<string> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    bool disposed, nativeStarted;
    public string Address { get; private set; } = "";
    public static async Task<IBluetoothHost> Start(int port, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsMacOS()) throw new IOException("Bluetooth is not available on this device.");
        Dispatcher.UIThread.VerifyAccess();
        if (current != null) throw new IOException("Bluetooth sharing is already running.");
        var bridge = new MacBluetooth(); current = bridge;
        try
        {
            pg_bluetooth_start(port, Callback); bridge.nativeStarted = true;
            bridge.Address = await bridge.ready.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellation);
            return bridge;
        }
        catch { bridge.Dispose(); throw; }
    }
    static void Receive(IntPtr json)
    {
        // Never allow a managed exception to cross the native callback boundary.
        try
        {
            var target = current;
            if (target == null || target.disposed) return;
            using var message = JsonDocument.Parse(Marshal.PtrToStringUTF8(json) ?? "{}");
            var state = message.RootElement.GetProperty("state").GetString();
            var detail = message.RootElement.GetProperty("detail").GetString() ?? "";
            if (state == "ready") target.ready.TrySetResult(detail);
            else if (state == "error")
            {
                if (!target.ready.TrySetException(new IOException(detail))) target.Interrupted?.Invoke(detail);
            }
        }
        catch (Exception ex) { current?.ready.TrySetException(ex); }
    }
    public event Action<string>? Interrupted;
    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (disposed) return; disposed = true;
        if (current == this) { current = null; if (nativeStarted) pg_bluetooth_stop(); }
        ready.TrySetCanceled();
    }
}
