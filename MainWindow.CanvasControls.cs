using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Papergraph;

public partial class MainWindow
{
    void InitializeCanvasControls()
    {
        void Text(Button button, string label) => Localization.Bind(button, ContentControl.ContentProperty, label);
        Text(CanvasSelectButton, "Select"); Text(CanvasActionsButton, "Actions");
        CanvasSelectButton.Click += (_, _) => SetCanvasTool(false);
        CanvasZoomOut.Click += (_, _) => ZoomCanvas(1 / 1.2);
        CanvasZoomIn.Click += (_, _) => ZoomCanvas(1.2);
        CanvasZoomReset.Click += (_, _) => ZoomCanvas(1 / Graph.Zoom);
        Localization.Bind(CanvasZoomOut, ToolTipProperty, "Zoom out");
        Localization.Bind(CanvasZoomIn, ToolTipProperty, "Zoom in");
        Localization.Bind(CanvasZoomReset, ToolTipProperty, "Reset zoom to 100%");
        CanvasActionsButton.Click += (_, _) => ShowContext(CanvasActionsButton, PlacementMode.Bottom);
        var select = LocalizedItem(ViewMenu, "Select", () => SetCanvasTool(false), "V"); select.IsCheckable = true;
        var pan = LocalizedItem(ViewMenu, "Pan", () => SetCanvasTool(true), "H"); pan.IsCheckable = true;
        var titles = LocalizedItem(ViewMenu, "Show titles", () => Graph.ShowCaptions = !Graph.ShowCaptions, "Shift+Space"); titles.IsCheckable = true;
        ViewMenu.SubmenuOpened += (_, _) => { select.IsChecked = !Graph.PanTool; pan.IsChecked = Graph.PanTool; titles.IsChecked = Graph.ShowCaptions; };
        Graph.ViewChanged += UpdateCanvasControls; Graph.SelectionChanged += UpdateCanvasControls;
        PreviewKeyUp += (_, e) => { if (e.Key == Key.Space) ReleaseSpacePan(); };
        Deactivated += (_, _) => ReleaseSpacePan();
        UpdateCanvasControls();
    }
    internal void SetCanvasTool(bool pan)
    {
        if (Graph.IsInteracting) return;
        CancelLink(); Graph.PanTool = pan; Graph.SpacePan = false; UpdateCanvasControls(); Graph.Focus();
    }
    internal void ReleaseSpacePan() { Graph.SpacePan = false; UpdateCanvasControls(); }
    void UpdateCanvasControls()
    {
        CanvasSelectButton.Visibility = Graph.PanTool ? Visibility.Visible : Visibility.Collapsed;
        CanvasZoomReset.Content = (Graph.Zoom * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        CanvasHint.Text = Localization.Text(Graph.PanTool || Graph.SpacePan ? "Drag to pan · wheel to zoom · V to select" :
            Graph.SelectedRegion != null ? "Drag the frame border to move · drag its lower-right corner to resize" :
            Graph.GroupSelection ? "Drag the selection to move · use the buttons below to group or delete" :
            "Drag empty canvas to pan · Shift-drag to select · wheel to zoom");
        if (!Graph.IsInteracting && !Graph.DrawingRegion && !Graph.LinkMode) Graph.Cursor = Graph.PanTool || Graph.SpacePan ? Cursors.ScrollAll : Cursors.Arrow;
    }
    internal void ZoomCanvas(double factor)
    {
        if (Graph.IsPreview || Graph.IsInteracting || !double.IsFinite(factor) || factor <= 0) return;
        var anchor = new Point(Graph.ActualWidth / 2, Graph.ActualHeight / 2); var world = Graph.ToWorld(anchor);
        var zoom = Math.Clamp(Graph.Zoom * factor, GraphSurface.MinimumZoom, GraphSurface.MaximumZoom);
        Graph.SetView(zoom, new Point(anchor.X - world.X * zoom, anchor.Y - world.Y * zoom));
    }
    internal bool RouteCanvasTool(Key key, ModifierKeys modifiers, IInputElement? focused)
    {
        if (modifiers != ModifierKeys.None || focused is TextBoxBase or MenuItem or MenuBase or RadioButton ||
            GraphTree.IsKeyboardFocusWithin || NotePageTabs.IsKeyboardFocusWithin || Graph.IsPreview || Graph.IsInteracting) return false;
        if (key == Key.H) SetCanvasTool(true);
        else if (key == Key.V) SetCanvasTool(false);
        else if (key == Key.Space) { Graph.SpacePan = true; UpdateCanvasControls(); }
        else return false;
        return true;
    }
}
