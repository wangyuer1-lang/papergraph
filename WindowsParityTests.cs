using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Papergraph;

public static class WindowsParityTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        var native = new BodyTextBox { Text = "ab" };
        // WPF's Enter command requires real OS keyboard focus. Exercise the
        // same document paragraph operation without taking the user's focus.
        native.Select(1, 0); native.CaretPosition.InsertParagraphBreak();
        Check(native.Text == "a\nb", "Native paragraph split inserts exactly one manuscript newline");
        native.Select(2, 1); native.Selection.Text = "中文";
        Check(native.Text == "a\n中文", "Selection offsets after a native paragraph break remain UTF-16 text offsets");
        native.SetColors([new(2, 2, "#D44747")]);
        Check(native.Text == "a\n中文", "Coloring across native paragraph structure never adds text");
        native.Select(1, 1); native.Selection.Text = "";
        Check(native.Text == "a中文", "Deleting a selected native paragraph break joins text without loss");
        native.Text = ""; native.Selection.Text = "🌱输入";
        Check(native.Text == "🌱输入", "An empty native editor accepts Unicode input without a synthetic newline");
        var root = Path.Combine(Path.GetTempPath(), "papergraph-windows-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "colors.papergraph");
        const string body = "甲乙🌱丙丁\r\nChinese 中文\n日本語\tend";
        var fixture = new GraphDocument { Title = "Windows · Mac writing parity", Nodes = [
            new() { Id = "first", Title = body, Caption = "Shared text colors", X = 100, Y = 100, BodyColors = [new(2, 2, "#D44747")] },
            new() { Id = "second", Title = "Second proposition", X = 480, Y = 100 }],
            Edges = [new() { Id = "flow", From = "first", To = "second" }],
            Regions = [new() { Id = "frame", Title = "Frame body", IsAbsolute = true, X = 60, Y = 50, Width = 680, Height = 350 }] };
        Storage.Save(path, fixture); File.WriteAllText(Path.Combine(root, "recent.txt"), "colors.papergraph");
        var window = new MainWindow(root); var editor = window.PropositionBox;
        var content = (FrameworkElement)window.Content;
        void Layout() { content.Measure(new Size(1380, 880)); content.Arrange(new Rect(0, 0, 1380, 880)); content.UpdateLayout(); }
        void Select(string id) { window.Graph.ClearAllSelection(); if (window.Graph.Document.Node(id) != null) window.Graph.Selected = [id]; else window.Graph.SelectedRegion = id; window.SelectionChanged(); Layout(); }
        Proposition Node() => window.Graph.Document.Node("first")!;
        IEnumerable<Run> Runs(FlowDocument document) => document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>());
        bool Red(Run run) => run.Foreground is SolidColorBrush b && b.Color == Color.FromRgb(0xD4, 0x47, 0x47);
        JsonElement Call(object request) => JsonSerializer.SerializeToElement(window.HandleAgent(JsonSerializer.SerializeToElement(request)), GraphDocument.Options);
        Select("first"); window.SetFullTextVisible(true, false);
        var originalGraph = window.Graph.Document.Serialize();
        Check(window.RouteCanvasTool(Key.H, ModifierKeys.None, window.Graph) && window.Graph.PanTool, "H enables persistent pan like Mac");
        Check(window.RouteCanvasTool(Key.V, ModifierKeys.None, window.Graph) && !window.Graph.PanTool, "V returns to selection");
        Check(window.RouteCanvasTool(Key.Space, ModifierKeys.None, window.Graph) && window.Graph.SpacePan, "Space enables temporary canvas pan");
        window.ReleaseSpacePan(); Check(!window.Graph.SpacePan, "Releasing Space restores the current tool");
        Check(!window.RouteCanvasTool(Key.Space, ModifierKeys.None, editor) && !window.RouteCanvasTool(Key.H, ModifierKeys.None, window.NotesBox), "Text editors retain ordinary Space and H input");
        var anchor = new Point(window.Graph.ActualWidth / 2, window.Graph.ActualHeight / 2); var world = window.Graph.ToWorld(anchor);
        window.ZoomCanvas(1.2); Check((window.Graph.ToScreen(world) - anchor).Length < .001, "Visible zoom controls preserve the canvas center");
        window.ZoomCanvas(1 / window.Graph.Zoom); Check(Math.Abs(window.Graph.Zoom - 1) < 1e-9, "Percentage control resets zoom to 100 percent");
        Check(window.Graph.Document.Serialize() == originalGraph, "Canvas tools and zoom do not alter manuscript data");
        Check(editor.Text == body && Node().Title == body, "Native colored editor preserves exact CRLF, LF, tab, Chinese, Japanese and emoji text");
        Check(Runs(editor.Document).Any(r => r.Text == "🌱" && Red(r)), "Mac color ranges render in the native Windows editor");
        Check(Runs(window.FullTextBox.Document).Any(r => r.Text == "🌱" && Red(r)), "Mac color ranges render in Windows full text");
        var plain = window.CurrentFullText.Text; var counts = window.FullTextCount.Text;
        editor.Select(0, 2); window.ColorBodySelection("#397FC4");
        Check(editor.SelectionStart == 0 && editor.SelectionLength == 2 && editor.Text == body, "Color controls retain selection and manuscript bytes");
        Check(Node().BodyColors!.SequenceEqual(new[] { new BodyTextColor(0, 2, "#397FC4"), new(2, 2, "#D44747") }), "Formatting changes only selected UTF-16 ranges");
        Check(window.CurrentFullText.Text == plain && window.FullTextCount.Text == counts, "Coloring leaves manuscript ordering and counts unchanged");
        ApplicationCommands.Undo.Execute(null, editor); Check(Node().BodyColors!.SequenceEqual(new[] { new BodyTextColor(2, 2, "#D44747") }), "Native editor Undo restores previous colors");
        ApplicationCommands.Redo.Execute(null, editor); Check(Node().BodyColors!.Count == 2, "Native editor Redo restores colors");
        editor.Select(0, 2); window.ColorBodySelection(null); Check(Node().BodyColors!.Single().Start == 2, "Clear color preserves adjacent formatting");
        window.UndoBody(false);
        var marked = Node().BodyColors!.ToArray();
        editor.Select(0, 0); editor.Selection.Text = "序";
        Check(Node().Title == "序" + body && Node().BodyColors!.All(r => r.Start >= 1), "Native text insertion shifts existing colors without rewriting line endings");
        window.UndoBody(false); Check(Node().Title == body && Node().BodyColors!.SequenceEqual(marked), "Editor undo restores text and its original formatting together");
        window.UndoBody(true); Check(Node().Title == "序" + body, "Editor redo restores inserted text"); window.UndoBody(false);
        editor.Select(2, 2); editor.Selection.Text = "新文";
        Check(Node().Title == body.Replace("🌱", "新文") && Node().BodyColors!.Any(r => r.Start == 2 && r.Length == 2 && r.Color == "#D44747"), "Replacing a colored emoji preserves the selected color"); window.UndoBody(false);
        editor.Select(2, 2); editor.Selection.Text = "";
        Check(Node().BodyColors!.All(r => r.Color != "#D44747") && Node().Title == body.Replace("🌱", ""), "Deleting colored text removes its formatting"); window.UndoBody(false);
        Select("second"); Check(!Runs(editor.Document).Any(Red), "Colors do not leak to a different proposition"); Select("first");
        Check(Runs(editor.Document).Any(r => r.Text == "🌱" && Red(r)), "Returning to a proposition restores its own colors");
        Select("frame"); editor.Select(0, 5); window.ColorBodySelection("#2B9464");
        Check(window.Graph.Document.Frame("frame")!.BodyColors!.Single() == new BodyTextColor(0, 5, "#2B9464"), "Frame body formatting matches the Mac editor"); Select("first");
        var request = new { operation = "editGraph", requestId = Guid.NewGuid().ToString(), expectedDocument = path,
            expectedRevision = AgentProtocol.Revision(window.Graph.Document), nodes = new[] { new { id = "first", bodyColors = new[] { new { start = 0, length = 2, color = "#9A63BE", text = "甲乙" } } } } };
        Call(request); Call(request);
        Check(Node().Title == body && Node().BodyColors!.Single().Color == "#9A63BE", "Agent formatting remains exact, durable and retry-safe");
        Check(File.ReadAllText(path) == window.Graph.Document.Serialize(), "Formatting is saved before agent success");
        window.SetGraphAgentReadOnly(path, true);
        var protectedText = window.Graph.Document.Serialize(); bool rejected = false;
        try { Call(request); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && window.Graph.Document.Serialize() == protectedText, "Windows protection rejects even a previously successful agent retry");
        Select("first"); editor.Select(2, 2); window.ColorBodySelection("#D44747");
        Check(Node().BodyColors!.Any(r => r.Color == "#D44747") && window.Graph.Document.AgentReadOnly, "Human coloring remains possible on an AI-protected graph");
        window.UndoBody(false); Check(window.Graph.Document.AgentReadOnly, "Undo never removes AI protection"); window.UndoBody(true);
        var snapshot = Call(new { operation = "snapshot" });
        Check(snapshot.GetProperty("agentReadOnly").GetBoolean() && !snapshot.GetProperty("agentWritable").GetBoolean(), "Live Windows snapshots expose current protection");
        Select("first"); editor.Select(0, 0);
        foreach (bool dark in new[] { false, true })
        {
            if (window.Graph.Dark != dark) window.ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.RefreshFullText(); Layout();
            Check(Runs(editor.Document).Any(r => r.Text == "🌱" && Red(r)), "Explicit colors survive theme changes");
            var bitmap = new RenderTargetBitmap(1380, 880, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.Combine(AppContext.BaseDirectory, "windows-parity-" + (dark ? "dark" : "light") + ".png")); encoder.Save(output);
        }
        var final = window.Graph.Document.Serialize(); window.Close();
        Check(GraphDocument.Parse(File.ReadAllText(path)).Serialize() == final, "Color ranges, Unicode and protection survive save and reload");
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "test-results.txt"), $"PASS: {checks} Windows/Mac parity checks: native colored editing, source text, full text, undo/redo, agent permissions, persistence and light/dark rendering.\n");
    }
}
