using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Papergraph;

// A native WPF editor with the same UTF-16 selection contract as the Mac editor.
// Only plain text and the model's color ranges are persisted. Paragraph markers
// are excluded from offsets, and existing Run text keeps its exact line endings.
public sealed class BodyTextBox : RichTextBox
{
    string text = "";
    bool formatting;
    sealed record TextPart(int Offset, int Length, TextPointer Start, bool Literal);
    List<TextPart> parts = [];

    public BodyTextBox()
    {
        IsUndoEnabled = false; AcceptsTab = false; AcceptsReturn = true;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document = CreateDocument("");
        ReadText();
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.FormatToApply = DataFormats.UnicodeText;
            else e.CancelCommand();
        });
        CommandManager.AddPreviewExecutedHandler(this, (_, e) =>
        {
            if (e.Command is RoutedUICommand command && command.OwnerType == typeof(EditingCommands) &&
                (command.Name.StartsWith("Toggle") || command.Name.StartsWith("Align") ||
                 command.Name.Contains("Font") || command.Name.Contains("Indent"))) e.Handled = true;
        });
    }

    FlowDocument CreateDocument(string value) => new(new Paragraph(new Run(value)) { Margin = new Thickness(0) })
    { PagePadding = new Thickness(0) };

    public string Text
    {
        get => text;
        set
        {
            value ??= "";
            if (text == value) return;
            formatting = true;
            try { Document = CreateDocument(value); text = ReadText(); }
            finally { formatting = false; }
            base.OnTextChanged(new TextChangedEventArgs(TextChangedEvent, UndoAction.None));
        }
    }

    string ReadText()
    {
        var result = new StringBuilder(); parts = [];
        var pointer = Document.ContentStart;
        Paragraph? previousParagraph = null;
        while (pointer.CompareTo(Document.ContentEnd) < 0)
        {
            var context = pointer.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var run = pointer.GetTextInRun(LogicalDirection.Forward);
                parts.Add(new(result.Length, run.Length, pointer, true));
                result.Append(run); pointer = pointer.GetPositionAtOffset(run.Length)!; continue;
            }
            if (context == TextPointerContext.ElementStart)
            {
                var element = pointer.GetAdjacentElement(LogicalDirection.Forward);
                if (element is Paragraph paragraph)
                {
                    if (previousParagraph != null) { parts.Add(new(result.Length, 1, previousParagraph.ContentEnd, false)); result.Append('\n'); }
                    previousParagraph = paragraph;
                }
                else if (element is LineBreak) { parts.Add(new(result.Length, 1, pointer, false)); result.Append('\n'); }
            }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward)!;
        }
        return result.ToString();
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (formatting) return;
        var value = ReadText();
        if (value == text) return;
        text = value;
        base.OnTextChanged(e);
    }

    TextPointer At(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        foreach (var part in parts)
            if (offset < part.Offset + part.Length) return part.Literal ? part.Start.GetPositionAtOffset(offset - part.Offset)! : part.Start;
        return Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
    }
    int Offset(TextPointer pointer)
    {
        foreach (var part in parts)
        {
            if (part.Start.CompareTo(pointer) >= 0) return part.Offset;
            if (part.Literal && part.Start.GetPositionAtOffset(part.Length)!.CompareTo(pointer) >= 0)
                return part.Offset + part.Start.GetOffsetToPosition(pointer);
        }
        return text.Length;
    }
    public int SelectionStart { get => Offset(Selection.Start); set => Select(value, SelectionLength); }
    public int SelectionLength { get => Offset(Selection.End) - SelectionStart; set => Select(SelectionStart, value); }
    public int CaretIndex { get => Offset(CaretPosition); set => Select(value, 0); }
    internal bool IsFormatting => formatting;
    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, text.Length); length = Math.Clamp(length, 0, text.Length - start);
        Selection.Select(At(start), At(start + length));
    }

    public void SetColors(IReadOnlyList<BodyTextColor>? colors)
    {
        int start = SelectionStart, length = SelectionLength;
        formatting = true;
        try
        {
            new TextRange(Document.ContentStart, Document.ContentEnd).ApplyPropertyValue(TextElement.ForegroundProperty, Foreground);
            ReadText();
            // Capture live native ranges before styling. WPF can merge as well
            // as split Runs, invalidating cached Run-relative offsets even when
            // styles are applied in reverse order.
            var foregroundRanges=(colors??[]).Select(c=>(Range:new TextRange(At(c.Start),At(c.End)),Brush:GraphStyle.Brush(c.Color))).ToArray();
            foreach(var item in foregroundRanges.Reverse())
                item.Range.ApplyPropertyValue(TextElement.ForegroundProperty,item.Brush);
            ReadText();
            Select(start, length);
        }
        finally { formatting = false; }
    }

    public void SetHighlight(IEnumerable<(int Start, int End)> ranges, Brush brush, bool reveal = false, IReadOnlyList<BodyTextColor>? backgrounds = null)
    {
        int start = SelectionStart, length = SelectionLength;
        formatting = true;
        try
        {
            new TextRange(Document.ContentStart, Document.ContentEnd).ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            ReadText();
            var sourceRanges=ranges.Select(r=>new TextRange(At(r.Start),At(r.End))).ToArray();
            foreach(var range in sourceRanges.Reverse())
                range.ApplyPropertyValue(TextElement.BackgroundProperty,brush);
            // Persistent citation backgrounds remain visible when their source
            // is selected; ordinary selection/caret rendering stays native WPF.
            ReadText();
            var backgroundRanges=(backgrounds??[]).Select(c=>(Range:new TextRange(At(c.Start),At(c.End)),Brush:GraphStyle.Brush(c.Color))).ToArray();
            foreach(var item in backgroundRanges.Reverse())
                item.Range.ApplyPropertyValue(TextElement.BackgroundProperty,item.Brush);
            ReadText(); Select(start, length);
            var first = ranges.FirstOrDefault();
            if (reveal && first.End > first.Start)
                (At(first.Start).Parent as FrameworkContentElement)?.BringIntoView();
        }
        finally { formatting = false; }
    }
}
