using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Papergraph;

// Bluetooth carries the same authenticated TLS protocol and merge transaction as LAN.
// Only the physical transport is platform-specific. No raw document travels unencrypted.
public sealed record BluetoothPairCode(string Address, byte[] Token, byte[] CertificateHash)
{
    public const string ServiceUuid = "720f72c6-a99e-4e75-84a4-e91747942067";
    public override string ToString()
    {
        var address = Convert.FromHexString(Address.Replace(":", "").Replace("-", ""));
        if (address.Length != 6 || Token.Length != 16 || CertificateHash.Length != 32) throw new InvalidDataException("Invalid Bluetooth pairing code.");
        return "BT1-" + Convert.ToBase64String([.. address, .. Token, .. CertificateHash]).Replace('+', '-').Replace('/', '_');
    }
    public static BluetoothPairCode Parse(string text)
    {
        try
        {
            text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            if (text.Length != 76 || !text.StartsWith("BT1-", StringComparison.Ordinal)) throw new FormatException();
            var bytes = Convert.FromBase64String(text[4..].Replace('-', '+').Replace('_', '/'));
            if (bytes.Length != 54 || bytes[..6].All(b => b == 0) || bytes[..6].All(b => b == 255)) throw new FormatException();
            return new(string.Join(":", bytes[..6].Select(b => b.ToString("X2"))), bytes[6..22], bytes[22..54]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new InvalidDataException("Invalid Bluetooth pairing code."); }
    }
}

public interface IBluetoothHost : IDisposable
{
    string Address { get; }
    event Action<string>? Interrupted;
}
public static class BluetoothSync
{
    public static Func<string, byte[]?>? QrImage { get; set; }
    public static Func<int, CancellationToken, Task<IBluetoothHost>>? StartHost { get; set; }
    public static Func<BluetoothPairCode, CancellationToken, Task<Stream>>? OpenStream { get; set; }
    // Android keeps the permission/pairing dialogs from being mistaken for leaving the app.
    public static bool SystemDialogActive { get; set; }
    public static async Task<LanConnection> Connect(string code, string deviceId, CancellationToken cancellation)
    {
        var pair = BluetoothPairCode.Parse(code);
        if (OpenStream == null) throw new IOException("Bluetooth is not available on this device.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        Stream transport;
        try { transport = await OpenStream(pair, timeout.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new IOException("Bluetooth connection timed out."); }
        // Opening the socket may need system pairing. Once open, a separate bounded TLS
        // handshake verifies the full fingerprint carried in the out-of-band code.
        return await LanConnection.ConnectStream(transport, pair.Token, pair.CertificateHash, deviceId, cancellation, TimeSpan.FromSeconds(60));
    }
}
