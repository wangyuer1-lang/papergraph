using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;

namespace Papergraph;

public sealed record LanHello(int Version, string Token, string DeviceId);
public sealed record LanProposal(LanOffer Offer, string Choice);
public sealed record LanResult(List<LanDocument>? Documents, string? Error);
public sealed record LanPairCode(IPAddress Address, int Port, byte[] Token, byte[] CertificateHash)
{
    public override string ToString()
    {
        var bytes = new byte[54]; Address.GetAddressBytes().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)Port);
        Token.CopyTo(bytes, 6); CertificateHash.CopyTo(bytes, 22);
        return "PG1-" + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static LanPairCode Parse(string code, bool allowLoopback = false)
    {
        try
        {
            code = code.Trim();
            if (!code.StartsWith("PG1-", StringComparison.Ordinal) || code.Length != 76) throw new FormatException();
            var bytes = Convert.FromBase64String(code[4..].Replace('-', '+').Replace('_', '/'));
            var address = new IPAddress(bytes.AsSpan(0, 4));
            int port = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
            if (bytes.Length != 54 || port == 0 || !LanWire.Private(address, allowLoopback)) throw new FormatException();
            return new(address, port, bytes[6..22], bytes[22..54]);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new InvalidDataException("Invalid pairing code. Copy the complete code from the other computer."); }
    }
}
public static class LanWire
{
    public const int MaxFrame = 64_000_000;
    public static bool Private(IPAddress ip, bool allowLoopback = false)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return allowLoopback && IPAddress.IsLoopback(ip) || b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
            b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254;
    }
    public static List<IPAddress> Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .Where(a => Private(a)).Distinct().ToList();
    public static async Task Send<T>(Stream stream, T message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        if (bytes.Length > MaxFrame) throw new InvalidDataException("This sync transfer is too large (64 MB).");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellation); await stream.WriteAsync(bytes, cancellation); await stream.FlushAsync(cancellation);
    }
    public static async Task<T> Read<T>(Stream stream, CancellationToken cancellation, int limit = MaxFrame)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellation);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 2 || length > limit) throw new InvalidDataException("Invalid sync message size.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancellation);
        return JsonSerializer.Deserialize<T>(bytes, new JsonSerializerOptions { MaxDepth = 64 }) ?? throw new InvalidDataException("Empty sync message.");
    }
}
public sealed class LanHost : IDisposable
{
    readonly TcpListener listener;
    readonly RSA key;
    readonly X509Certificate2 certificate;
    readonly CancellationTokenSource lifetime = new(TimeSpan.FromMinutes(10));
    readonly Func<string, Task<LanOffer>> capture;
    readonly Func<LanOffer, LanOffer, string, LanPlan, Task<bool>> approve;
    readonly Func<LanOffer, string, IReadOnlyList<LanDocument>, Task> apply;
    readonly Action<string> status;
    readonly bool allowLoopback;
    readonly TimeSpan handshakeTimeout;
    bool disposed;
    public LanPairCode Code { get; }
    public Task Completion { get; }
    public LanHost(IPAddress address, Func<string, Task<LanOffer>> capture,
        Func<LanOffer, LanOffer, string, LanPlan, Task<bool>> approve,
        Func<LanOffer, string, IReadOnlyList<LanDocument>, Task> apply, Action<string> status, bool allowLoopback = false, TimeSpan? handshakeTimeout = null)
    {
        if (!LanWire.Private(address, allowLoopback)) throw new InvalidDataException("Select a local network address.");
        this.capture = capture; this.approve = approve; this.apply = apply; this.status = status; this.allowLoopback = allowLoopback; this.handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(10);
        key = RSA.Create(2048);
        var request = new CertificateRequest("CN=papergraph.local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        if (OperatingSystem.IsWindows())
        {
            // Schannel cannot use the ephemeral key attached by CreateSelfSigned.
            // Import into the current user's temporary key container; without
            // PersistKeySet it is removed when the certificate is disposed.
            var pfx = generated.Export(X509ContentType.Pkcs12);
            try { certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
            finally { CryptographicOperations.ZeroMemory(pfx); }
        }
        else certificate = new X509Certificate2(generated);
        listener = new TcpListener(address, 0); listener.Start(4);
        Code = new(address, ((IPEndPoint)listener.LocalEndpoint).Port, RandomNumberGenerator.GetBytes(16), SHA256.HashData(certificate.RawData));
        Completion = Run();
    }
    async Task Run()
    {
        try
        {
            // Only ten unauthenticated attempts are allowed per ten-minute code.
            for (int attempt = 0; attempt < 10; attempt++)
            {
                using var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                if (client.Client.RemoteEndPoint is not IPEndPoint remote || !LanWire.Private(remote.Address, allowLoopback)) continue;
                using var ssl = new SslStream(client.GetStream(), false);
                LanHello hello;
                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    handshake.CancelAfter(handshakeTimeout);
                    try
                    {
                        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            ClientCertificateRequired = false, CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        }, handshake.Token);
                        hello = await LanWire.Read<LanHello>(ssl, handshake.Token, 4096);
                        if (hello.Version != 1 || !LanMerge.ValidId(hello.DeviceId) || hello.Token == null ||
                            !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(hello.Token), System.Text.Encoding.UTF8.GetBytes(Convert.ToHexString(Code.Token)))) continue;
                    }
                    catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or JsonException or InvalidDataException) { continue; }
                }
                // The code is single-use after successful authentication.
                listener.Stop();
                var host = await capture(hello.DeviceId);
                if (host.DeviceId == hello.DeviceId) throw new InvalidDataException("The other library has the same device identity.");
                await LanWire.Send(ssl, host, lifetime.Token);
                var proposal = await LanWire.Read<LanProposal>(ssl, lifetime.Token);
                if (proposal.Offer == null || proposal.Offer.DeviceId != hello.DeviceId) throw new InvalidDataException("The paired device changed.");
                var plan = LanMerge.Build(host, proposal.Offer, proposal.Choice);
                if (!await approve(host, proposal.Offer, proposal.Choice, plan).WaitAsync(lifetime.Token))
                {
                    await LanWire.Send(ssl, new LanResult(null, "The other computer cancelled sync."), lifetime.Token); return;
                }
                try { await apply(host, proposal.Offer.DeviceId, plan.Documents); }
                catch (Exception ex)
                {
                    await LanWire.Send(ssl, new LanResult(null, ex.Message), lifetime.Token); throw;
                }
                await LanWire.Send(ssl, new LanResult(plan.Documents, null), lifetime.Token);
                status("Saved locally. Waiting for the other computer…");
                var receipt = await LanWire.Read<string>(ssl, lifetime.Token, 1024);
                if (receipt != "saved") throw new IOException("The other computer did not confirm saving.");
                status("Sync complete."); return;
            }
            status("Pairing expired. Create a new code.");
        }
        catch (OperationCanceledException) { if (!disposed) status("Pairing expired. Create a new code."); }
        catch (Exception ex) { if (!disposed) status(Localization.Text("Connection interrupted. Reconnect to compare again.") + "\n" + Localization.Text(ex.Message)); }
        finally { listener.Stop(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); listener.Stop();
        _ = Completion.ContinueWith(_ => { certificate.Dispose(); key.Dispose(); lifetime.Dispose(); }, TaskScheduler.Default);
    }
}
public sealed class LanConnection : IDisposable
{
    readonly SslStream stream;
    public LanOffer Host { get; }
    LanConnection(SslStream stream, LanOffer host) { this.stream = stream; Host = host; }
    public static async Task<LanConnection> Connect(string code, string deviceId, CancellationToken cancellation, bool allowLoopback = false)
    {
        var pair = LanPairCode.Parse(code, allowLoopback); var client = new TcpClient();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(pair.Address, pair.Port, timeout.Token);
            return await ConnectStream(client.GetStream(), pair.Token, pair.CertificateHash, deviceId, timeout.Token, TimeSpan.FromSeconds(15));
        }
        catch { client.Dispose(); throw; }
    }
    // Takes ownership of transport, including on failed authentication or cancellation.
    public static async Task<LanConnection> ConnectStream(Stream transport, byte[] token, byte[] certificateHash,
        string deviceId, CancellationToken cancellation, TimeSpan handshakeTimeout)
    {
        var ssl = new SslStream(transport, false, (_, cert, _, _) =>
            cert != null && CryptographicOperations.FixedTimeEquals(SHA256.HashData(cert.GetRawCertData()), certificateHash));
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(handshakeTimeout);
            // Native Bluetooth reads may block; closing the socket guarantees cancellation.
            using var close = timeout.Token.Register(() => { try { transport.Dispose(); } catch { } });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "papergraph.local", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);
            await LanWire.Send(ssl, new LanHello(1, Convert.ToHexString(token), deviceId), timeout.Token);
            var host = await LanWire.Read<LanOffer>(ssl, timeout.Token); host.Validate();
            return new(ssl, host);
        }
        catch { ssl.Dispose(); throw; }
    }
    public async Task<List<LanDocument>> Propose(LanOffer guest, string choice, CancellationToken cancellation)
    {
        var expected = LanMerge.Build(Host, guest, choice);
        await LanWire.Send(stream, new LanProposal(guest, choice), cancellation);
        var result = await LanWire.Read<LanResult>(stream, cancellation);
        if (result.Error != null) throw new IOException(result.Error);
        if (result.Documents == null || !result.Documents.OrderBy(d => d.Id).Select(d => d.Id + d.Hash)
            .SequenceEqual(expected.Documents.OrderBy(d => d.Id).Select(d => d.Id + d.Hash)))
            throw new InvalidDataException("The sync result did not match the preview.");
        return result.Documents;
    }
    public Task Acknowledge(CancellationToken cancellation) => LanWire.Send(stream, "saved", cancellation);
    public void Dispose() { stream.Dispose(); }
}
