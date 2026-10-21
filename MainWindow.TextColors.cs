using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Papergraph;

public partial class MainWindow
{
    readonly List<Button> textColorButtons = [];
    IGraphBody? SelectedBody => editorKind switch
    { "node" => doc.Node(editorId ?? ""), "region" => doc.Frame(editorId ?? ""), _ => null };

    void InitializeTextColors()
    {
        var label = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Localization.Bind(label, TextBlock.TextProperty, "Text color"); TextColorToolbar.Children.Add(label);
        foreach (var (name, color) in GraphTextColors.Palette)
        {
            var button = new Button { Width = 25, Height = 27, Padding = new Thickness(5), Focusable = false,
                Content = new Border { Width = 13, Height = 13, CornerRadius = new CornerRadius(7), Background = GraphStyle.Brush(color) } };
            Localization.Bind(button, ToolTipProperty, name);
            Localization.Bind(button, System.Windows.Automation.AutomationProperties.NameProperty, name);
            button.Click += (_, _) => ColorBodySelection(color); textColorButtons.Add(button); TextColorToolbar.Children.Add(button);
        }
        var clear = new Button { FontSize = 11, Height = 27, Padding = new Thickness(4, 3, 4, 3), Focusable = false };
        Localization.Bind(clear, ContentControl.ContentProperty, "Clear color");
        clear.Click += (_, _) => ColorBodySelection(null); textColorButtons.Add(clear); TextColorToolbar.Children.Add(clear);
        Localization.Bind(TextColorToolbar, ToolTipProperty, "Select proposition text, then choose a color");
        PropositionBox.SelectionChanged += (_, _) => UpdateTextColorToolbar();
        PropositionBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
            (_, e) => { UndoBody(false); e.Handled = true; }, (_, e) => { e.CanExecute = undo.Count > 0; e.Handled = true; }));
        PropositionBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
            (_, e) => { UndoBody(true); e.Handled = true; }, (_, e) => { e.CanExecute = redo.Count > 0; e.Handled = true; }));
    }
    void UpdateTextColorToolbar()
    {
        foreach (var button in textColorButtons) button.IsEnabled = SelectedBody != null && PropositionBox.SelectionLength > 0;
    }
    internal void ColorBodySelection(string? color)
    {
        if (SelectedBody is not { } item || PropositionBox.SelectionLength == 0) return;
        var next = GraphTextColors.Apply(item.Title, item.BodyColors, PropositionBox.SelectionStart, PropositionBox.SelectionLength, color);
        if ((item.BodyColors ?? []).SequenceEqual(next ?? [])) return;
        Remember(); item.BodyColors = next; PropositionBox.SetColors(next); Changed(false); RefreshFullText(); PropositionBox.Focus();
    }
    internal void UndoBody(bool forward)
    {
        var id = editorId; var kind = editorKind;
        int start = PropositionBox.SelectionStart, length = PropositionBox.SelectionLength;
        if (forward) Redo(); else Undo();
        if (id != null && doc.HasEndpoint(id))
        {
            Graph.ClearAllSelection(); if (kind == "node") Graph.Selected = [id]; else Graph.SelectedRegion = id;
            SelectionChanged(); PropositionBox.Select(start, length); PropositionBox.Focus();
        }
    }
    void SetText(BodyTextBox box, string text) => box.Text = text;
}
