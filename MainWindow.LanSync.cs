using System.IO;
using System.Threading.Tasks;

namespace Papergraph;

public partial class MainWindow
{
    LanSyncDialog? lanDialog;
    LanSyncStore? lanStore;
    void OpenLanSync()
    {
        if (lanDialog != null) { lanDialog.Activate(); return; }
        try
        {
            Graph.CancelLayout();
            if (Graph.IsInteracting || !Save()) return;
            lanStore ??= new LanSyncStore(dataDir);
            lanDialog = new LanSyncDialog(lanStore.DeviceId, CaptureLan, ApplyLan, OpenLanBackups);
            foreach (var key in Resources.Keys) lanDialog.Resources[key] = Resources[key];
            lanDialog.Closed += (_, _) => lanDialog = null;
#if CROSS_PLATFORM
            lanDialog.RequestedThemeVariant = RequestedThemeVariant;
            _ = lanDialog.ShowDialog(this);
#else
            lanDialog.Owner = this; lanDialog.ShowDialog();
#endif
        }
        catch (System.Exception ex) { Notify(ex.Message); }
    }
    void OpenLanBackups()
    {
        var folder = Path.Combine(dataDir, ".lan-sync", "backups");
        Directory.CreateDirectory(folder);
        var start = new System.Diagnostics.ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/open" : OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(folder);
        System.Diagnostics.Process.Start(start);
    }
    LanOffer CaptureLan(string peerId)
    {
        Graph.CancelLayout();
        if (Graph.IsInteracting || !Save()) throw new IOException(Localization.Text("Save your changes before syncing."));
        return lanStore!.Capture(library.Select(e => e.Path), catalog, peerId);
    }
    void ApplyLan(LanOffer expected, string peerId, IReadOnlyList<LanDocument> documents)
    {
        var current = CaptureLan(peerId);
        if (current.Revision != expected.Revision) throw new IOException(Localization.Text("Your library changed after the preview. Reconnect to compare it again."));
        var applied = lanStore!.Apply(expected, peerId, documents, library.Select(e => e.Path), catalog);
        var active = file;
        catalog = applied.Catalog;
        library.Clear();
        foreach (var path in applied.Paths)
        {
            var saved = GraphDocument.Parse(File.ReadAllText(path));
            library.Add(new(path, DisplayTitle(saved.Title), saved.AgentReadOnly));
        }
        // Clear stale Undo snapshots only if the open document actually changed.
        var next = GraphDocument.Parse(File.ReadAllText(active));
        if (next.Serialize() != doc.Serialize()) ResetDocument(next, active, false);
        else { RefreshLibraryTree(true); RefreshAll(); }
    }
}
