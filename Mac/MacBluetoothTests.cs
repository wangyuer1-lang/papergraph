using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace Papergraph;

public partial class MainWindow
{
    internal async Task RunBluetoothUiTests()
    {
        OpenLanSync();
        var dialog = lanDialog ?? throw new Exception("Sync dialog did not open.");
        var start = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, Localization.Text("Create Bluetooth code (Android)")));
        start.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        TextBox? input = null;
        for (int i = 0; i < 900; i++)
        {
            input = dialog.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(b => (b.Text ?? "").StartsWith("BT1-"));
            if (input != null) break;
            await Task.Delay(100);
        }
        if (input == null)
            throw new Exception("Bluetooth code was not generated: " + string.Join(" | ", dialog.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text)));
        var code = input.Text!; BluetoothPairCode.Parse(code);
        var png = BluetoothSync.QrImage!(code) ?? throw new Exception("QR image missing.");
        File.WriteAllBytes(Path.Combine(Program.DataDirectory, "bluetooth-qr.png"), png);
        File.WriteAllText(Path.Combine(Program.DataDirectory, "bluetooth-code.txt"), code);
        dialog.UpdateLayout();
        using (var bitmap = new RenderTargetBitmap(new PixelSize((int)dialog.Bounds.Width, (int)dialog.Bounds.Height), new Vector(96, 96)))
        { bitmap.Render(dialog); bitmap.Save(Path.Combine(Program.DataDirectory, "bluetooth-mac.png")); }
        // Start/stop/restart validates transient service cleanup with the real Mac adapter.
        var reset = dialog.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, Localization.Text("Disconnect / new pairing")));
        reset.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        await Task.Delay(100);
        start.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        for (int i = 0; i < 100 && !(input.Text ?? "").StartsWith("BT1-"); i++) await Task.Delay(100);
        if (!(input.Text ?? "").StartsWith("BT1-") || code == input.Text) throw new Exception("Bluetooth restart failed to rotate the pairing secret.");
        dialog.Close();
        File.WriteAllText(Path.Combine(Program.DataDirectory, "bluetooth-native-results.txt"), "PASS: Mac Bluetooth permission, transient RFCOMM registration, QR generation, stop and restart with a fresh secret.\nPhysical Android radio connection still requires a real phone test.\n");
    }
}
