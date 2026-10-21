using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if CROSS_PLATFORM
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
#endif

namespace Papergraph;

public sealed class LanSyncDialog : Window
{
#if CROSS_PLATFORM
    readonly Border qr = new() { Background = Brushes.White, Padding = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
    Avalonia.Media.Imaging.Bitmap? qrBitmap;
#endif
    readonly string deviceId;
    readonly Func<string, LanOffer> capture;
    readonly Action<LanOffer, string, IReadOnlyList<LanDocument>> apply;
    readonly CancellationTokenSource lifetime = new();
    readonly TextBox code = new(), preview = new();
    readonly TextBlock status = new();
    readonly ComboBox network = new(), conflict = new();
    readonly Button start, connect, sync, reset;
    readonly Button? bluetooth;
    IBluetoothHost? radio;
    CancellationTokenSource? radioCancellation;
    LanHost? host;
    LanConnection? connection;
    LanOffer? guest;
    TaskCompletionSource<bool>? approval;
    bool closed;
    int generation;
    string Choice => conflict.SelectedIndex switch { 1 => "guest", 2 => "host", _ => "both" };
    static string T(string text) => Localization.Text(text);
    public LanSyncDialog(string deviceId, Func<string, LanOffer> capture, Action<LanOffer, string, IReadOnlyList<LanDocument>> apply, Action openBackups)
    {
        this.deviceId = deviceId; this.capture = capture; this.apply = apply;
        Title = T(BluetoothSync.StartHost != null ? "Device sync" : "Local network sync"); Width = 800; Height = 760; MinWidth = 620; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(Text(BluetoothSync.StartHost != null ? "Use the same Wi-Fi, or connect Android to Mac with Bluetooth. Both methods sync all graphs and categories." : "Connect both computers to the same Wi-Fi. Sync includes all graphs and their categories."));
        panel.Children.Add(Text("Create a code on one computer, then paste it on the other. Nothing changes until both computers confirm."));
        panel.Children.Add(Text("This computer's network"));
        var addresses = LanWire.Addresses();
#if CROSS_PLATFORM
        network.ItemsSource = addresses;
#else
        network.ItemsSource = addresses;
#endif
        network.SelectedIndex = addresses.Count > 0 ? 0 : -1; network.Margin = new Thickness(0, 3, 0, 10);
        panel.Children.Add(network);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        start = MakeButton("Create pairing code", () => _ = Start());
        connect = MakeButton("Connect and compare", () => _ = Connect());
        reset = MakeButton("Disconnect / new pairing", Reset);
        row.Children.Add(start); row.Children.Add(connect); row.Children.Add(reset); panel.Children.Add(row);
        if (BluetoothSync.StartHost != null)
        {
            bluetooth = MakeButton("Create Bluetooth code (Android)", () => _ = Start(true));
            bluetooth.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(bluetooth);
            panel.Children.Add(Text("No shared Wi-Fi? Turn on Bluetooth on both devices. Paste the BT1- code into Papergraph on Android, and confirm any system pairing prompts. Keep both apps open."));
        }
        panel.Children.Add(Text("Pairing code (paste the complete code here)"));
        code.Margin = new Thickness(0, 4, 0, 4); code.MinHeight = 36;
#if CROSS_PLATFORM
        code.Classes.Add("outline"); preview.Classes.Add("outline");
        code.Watermark = "PG1-…";
#endif
        panel.Children.Add(code);
        var copy = MakeButton("Copy pairing code", () => _ = CopyCode()); panel.Children.Add(copy);
        panel.Children.Add(Text("The code expires after 10 minutes. Close this window to stop sharing."));
        status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 12, 0, 8);
        status.Text = T("Ready. Your files stay on your computers."); panel.Children.Add(status);
#if CROSS_PLATFORM
        panel.Children.Add(qr);
#endif
        preview.IsReadOnly = true; preview.AcceptsReturn = true; preview.TextWrapping = TextWrapping.Wrap;
        preview.Height = 170; preview.Margin = new Thickness(0, 0, 0, 10);
        preview.Text = T("Differences will appear here after pairing.");
#if CROSS_PLATFORM
        ScrollViewer.SetVerticalScrollBarVisibility(preview, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        conflict.ItemsSource = new[] { T("Keep both versions (recommended)"), T("Use this computer; back up the other version"), T("Use the other computer; back up this version") };
#else
        preview.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        conflict.ItemsSource = new[] { T("Keep both versions (recommended)"), T("Use this computer; back up the other version"), T("Use the other computer; back up this version") };
#endif
        panel.Children.Add(preview);
        panel.Children.Add(Text("If the same content changed on both computers (applies to each whole conflicting graph)"));
        conflict.SelectedIndex = 0; conflict.IsEnabled = false; conflict.Margin = new Thickness(0, 4, 0, 10);
        conflict.SelectionChanged += (_, _) => { if (connection != null && guest != null) ShowPlan(connection.Host, guest, LanMerge.Build(connection.Host, guest, Choice), false); };
        panel.Children.Add(conflict);
        sync = MakeButton("Sync", () => _ = Synchronize()); sync.IsEnabled = false; panel.Children.Add(sync);
        panel.Children.Add(Text("Deleted graphs are restored from the other computer. First-time copies with no shared history are kept separately. Backups are saved locally."));
        panel.Children.Add(MakeButton("Open sync backups", () => { try { openBackups(); } catch (Exception ex) { status.Text = T(ex.Message); } }));
        Content = new ScrollViewer { Content = panel };
        Closed += (_, _) => { closed = true; approval?.TrySetResult(false); lifetime.Cancel(); host?.Dispose(); radioCancellation?.Cancel(); radio?.Dispose(); connection?.Dispose(); lifetime.Dispose();
#if CROSS_PLATFORM
            qrBitmap?.Dispose();
#endif
        };
    }
    static TextBlock Text(string text) => new() { Text = T(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    static Button MakeButton(string text, Action action)
    {
        var b = new Button { Content = T(text), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => action(); return b;
    }
    async Task<TValue> Ui<TValue>(Func<TValue> action)
    {
#if CROSS_PLATFORM
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(action);
#else
        return await Dispatcher.InvokeAsync(action);
#endif
    }
    void Status(string message)
    {
        if (closed) return;
#if CROSS_PLATFORM
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!closed) { status.Text = T(message); reset.IsEnabled = true; } });
#else
        Dispatcher.BeginInvoke(() => { if (!closed) { status.Text = T(message); reset.IsEnabled = true; } });
#endif
    }
    void Reset()
    {
        generation++;
        radioCancellation?.Cancel(); radioCancellation?.Dispose(); radioCancellation = null;
        radio?.Dispose(); radio = null;
#if CROSS_PLATFORM
        qr.IsVisible = false; qr.Child = null; qrBitmap?.Dispose(); qrBitmap = null;
#endif
        if (bluetooth != null) bluetooth.IsEnabled = true;
        approval?.TrySetResult(false); approval = null; host?.Dispose(); host = null;
        connection?.Dispose(); connection = null; guest = null;
        start.IsEnabled = connect.IsEnabled = network.IsEnabled = true;
        code.IsReadOnly = false; code.Text = ""; preview.Text = ""; sync.IsEnabled = conflict.IsEnabled = false;
        sync.Content = T("Sync"); status.Text = T("Ready. Your files stay on your computers.");
    }
    async Task Start(bool useBluetooth = false)
    {
        int session = -1;
        try
        {
            Reset();
            session = generation;
            var address = useBluetooth ? IPAddress.Loopback : network.SelectedItem as IPAddress;
            if (address == null) throw new InvalidOperationException(T("Connect to Wi-Fi or Ethernet first."));
            start.IsEnabled = connect.IsEnabled = network.IsEnabled = false;
            if (bluetooth != null) bluetooth.IsEnabled = false;
            code.IsReadOnly = true;
            if (useBluetooth) status.Text = T("Starting Bluetooth… Allow Papergraph to use Bluetooth if prompted.");
            host = new LanHost(address, peer => Ui(() => capture(peer)),
                async (a, b, choice, plan) => await await Ui(() =>
                {
                    if (closed || session != generation) return Task.FromResult(false);
                    ShowPlan(a, b, plan, true);
                    status.Text = T("The other computer requested sync. Review the differences and accept.");
                    status.Text += "\n" + T("Conflict handling") + ": " + T(choice == "both" ? "Keep both versions (recommended)" : choice == "host" ? "Use the hosting computer" : "Use the connecting computer");
                    approval = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    sync.Content = T("Accept and sync"); sync.IsEnabled = true;
                    return approval.Task;
                }),
                (expected, peer, docs) => Ui(() => { if (closed || session != generation) throw new OperationCanceledException(); apply(expected, peer, docs); return true; }),
                message => { if (session == generation) Status(message); }, useBluetooth, useBluetooth ? TimeSpan.FromSeconds(60) : null);
            var running = host;
            if (useBluetooth)
            {
                radioCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var started = await BluetoothSync.StartHost!(host.Code.Port, radioCancellation.Token);
                if (closed || session != generation) { started.Dispose(); return; }
                radio = started;
                radio.Interrupted += message => { if (!closed && session == generation) { Reset(); status.Text = T(message); } };
                code.Text = new BluetoothPairCode(radio.Address, host.Code.Token, host.Code.CertificateHash).ToString();
#if CROSS_PLATFORM
                var png = BluetoothSync.QrImage?.Invoke(code.Text);
                if (png != null) { qrBitmap = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(png)); qr.Child = new Image { Source = qrBitmap, Width = 216, Height = 216 }; qr.IsVisible = true; }
#endif
                _ = ReleaseRadioOnCompletion(running, session);
            }
            else code.Text = host.Code.ToString();
            code.IsReadOnly = true;
            start.IsEnabled = connect.IsEnabled = network.IsEnabled = false;
#if CROSS_PLATFORM
            if (useBluetooth) Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!closed && session == generation) qr.BringIntoView(); }, Avalonia.Threading.DispatcherPriority.Background);
#endif
            status.Text = T(useBluetooth ? "Bluetooth is ready. Paste this BT1- code on Android and keep this window open. Confirm the same system pairing number on both devices if asked." : "Waiting for the other computer. Copy the complete pairing code.");
        }
        catch (Exception ex)
        {
            if (closed || session != generation) return;
            Reset(); status.Text = T(ex is TimeoutException ? "Bluetooth did not become ready. Check Bluetooth permissions and try again." : ex.Message);
        }
    }
    async Task ReleaseRadioOnCompletion(LanHost running, int session)
    {
        await running.Completion;
        await Ui(() => { if (session == generation) { radio?.Dispose(); radio = null; } return true; });
    }
    async Task Connect()
    {
        try
        {
            start.IsEnabled = connect.IsEnabled = network.IsEnabled = reset.IsEnabled = false;
            if (bluetooth != null) bluetooth.IsEnabled = false;
            status.Text = T("Connecting…");
            connection = await LanConnection.Connect(code.Text ?? "", deviceId, lifetime.Token);
            guest = capture(connection.Host.DeviceId);
            conflict.IsEnabled = true; code.IsReadOnly = true;
            ShowPlan(connection.Host, guest, LanMerge.Build(connection.Host, guest, Choice), false);
            status.Text = T("Paired. Review the differences, then click Sync.");
            sync.IsEnabled = true;
        }
        catch (Exception ex)
        {
            connection?.Dispose(); connection = null;
            if (!closed) { status.Text = T("Could not connect. Check the code, Wi-Fi and local-network/firewall permissions.") + "\n" + T(ex.Message); start.IsEnabled = connect.IsEnabled = network.IsEnabled = true; }
        }
        finally { if (!closed) { reset.IsEnabled = true; if (bluetooth != null && connection == null) bluetooth.IsEnabled = true; } }
    }
    async Task Synchronize()
    {
        if (approval != null)
        {
            sync.IsEnabled = false; reset.IsEnabled = false;
            approval.TrySetResult(true); approval = null;
            status.Text = T("Syncing…"); return;
        }
        if (connection == null || guest == null) return;
        try
        {
            sync.IsEnabled = conflict.IsEnabled = reset.IsEnabled = false;
            status.Text = T("Waiting for confirmation on the other computer…");
            var documents = await connection.Propose(guest, Choice, lifetime.Token);
            apply(guest, connection.Host.DeviceId, documents);
            await connection.Acknowledge(lifetime.Token);
            status.Text = T("Sync complete.");
        }
        catch (Exception ex) { if (!closed) status.Text = T("Sync did not finish on both computers. Reconnect to compare again; backups were kept.") + "\n" + T(ex.Message); }
        finally { if (!closed) reset.IsEnabled = true; }
    }
    async Task CopyCode()
    {
        try
        {
#if CROSS_PLATFORM
            if (Clipboard != null) await Clipboard.SetTextAsync(code.Text ?? "");
#else
            System.Windows.Clipboard.SetText(code.Text ?? ""); await Task.CompletedTask;
#endif
        }
        catch (Exception ex) { status.Text = T(ex.Message); }
    }
    void ShowPlan(LanOffer a, LanOffer b, LanPlan plan, bool hosting)
    {
#if CROSS_PLATFORM
        qr.IsVisible = false;
#endif
        var text = new StringBuilder();
        text.AppendLine(T("Hosting computer") + ": " + a.DeviceName);
        text.AppendLine(T("Connecting computer") + ": " + b.DeviceName);
        text.AppendLine(T("Graphs to review") + ": " + plan.Changes.Count + " · " + T("Conflicts") + ": " + plan.Conflicts);
        if (plan.Changes.Count == 0) text.AppendLine(T("Both libraries already match."));
        foreach (var change in plan.Changes)
        {
            text.AppendLine("\n" + change.Title + " — " + T(change.Kind));
            foreach (var diff in change.Differences.Take(30))
            {
                text.AppendLine(T("Changed content") + ": " + T(diff.Path.Split('/').Last()));
                text.AppendLine(T("Last synced version") + ": " + Excerpt(diff.Before, diff.Host));
                text.AppendLine(a.DeviceName + ": " + Excerpt(diff.Host, diff.Before));
                text.AppendLine(b.DeviceName + ": " + Excerpt(diff.Guest, diff.Before));
            }
            if (change.Differences.Count > 30) text.AppendLine(T("More differences exist. Keep both versions to inspect them in full."));
        }
        preview.Text = text.ToString();
        preview.BringIntoView();
    }
    static string Excerpt(string text, string other)
    {
        if (text.Length <= 700) return text;
        int at = 0; while (at < Math.Min(text.Length, other.Length) && text[at] == other[at]) at++;
        int start = Math.Max(0, at - 80), end = Math.Min(text.Length, at + 500);
        if (start > 0 && char.IsLowSurrogate(text[start])) start--;
        if (end < text.Length && end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        return (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
    }
}
