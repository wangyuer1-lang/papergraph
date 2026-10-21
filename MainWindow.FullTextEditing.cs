using System.Windows.Input;
using System.Windows.Threading;

namespace Papergraph;

public partial class MainWindow
{
    FullTextEditBuffer? fullTextBuffer;
    long fullTextBufferRevision=-1;
    bool buildingFullText,editingFullText,fullTextComposing;
    int fullTextSelectionStart,fullTextSelectionEnd;

    void InitializeFullTextEditing()
    {
        Localization.Bind(FullTextBox,ToolTipProperty,"Click to edit. Changes are saved to the source proposition.");
        ConfigureTextMenu(FullTextBox);
        FullTextBox.TextChanged+=(_,_)=>{if(!buildingFullText&&!fullTextComposing)EditFullText();};
        FullTextBox.SelectionChanged+=(_,_)=>{CaptureFullTextSelection();RefreshFullTextCount();};
        FullTextBox.GotKeyboardFocus+=(_,_)=>{if(fullTextBufferRevision!=revision)RefreshFullText();};
        FullTextBox.LostKeyboardFocus+=(_,_)=>{fullTextComposing=false;EditFullText();editKey="";};
        FullTextBox.PreviewMouseLeftButtonUp+=(_,_)=>
        {
            if(FullTextBox.SelectionLength==0&&fullTextBuffer?.At(FullTextBox.CaretIndex) is {} span)
                FocusTextObject(span.NodeId,false);
        };
        // Keep the native IME's document and selection untouched until its
        // composition commits; provisional characters do not enter graph undo.
        TextCompositionManager.AddPreviewTextInputStartHandler(FullTextBox,(_,_)=>fullTextComposing=true);
        TextCompositionManager.AddPreviewTextInputHandler(FullTextBox,(_,_)=>
        {
            fullTextComposing=false;
            Dispatcher.BeginInvoke(()=>EditFullText(),DispatcherPriority.Send);
        });
        FullTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo,
            (_,e)=>{UndoFullText(false);e.Handled=true;},(_,e)=>{e.CanExecute=undo.Count>0;e.Handled=true;}));
        FullTextBox.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo,
            (_,e)=>{UndoFullText(true);e.Handled=true;},(_,e)=>{e.CanExecute=redo.Count>0;e.Handled=true;}));
    }
    void CaptureFullTextSelection()
    {
        if(buildingFullText||editingFullText||fullTextComposing||FullTextBox.IsFormatting)return;
        fullTextSelectionStart=FullTextBox.SelectionStart;
        fullTextSelectionEnd=fullTextSelectionStart+FullTextBox.SelectionLength;
    }
    void EditFullText()
    {
        if(buildingFullText||editingFullText||fullTextComposing||fullTextBuffer==null||FullTextBox.Text==fullTextBuffer.Text)return;
        if(fullTextDocument!=file||fullTextBufferRevision!=revision||!fullTextBuffer.Matches(doc))
        {
            Dispatcher.BeginInvoke(()=>RefreshFullText(),DispatcherPriority.Send);
            Notify(Localization.Text("The source changed. Review the refreshed text and try again."));return;
        }
        int start=fullTextSelectionStart,end=fullTextSelectionEnd;
        if(!fullTextBuffer.TryPrepare(FullTextBox.Text,start,end,editorId,out var edit))
        {
            // WPF is still inside a native document change here. Restore only
            // after that change block ends, never swap Document from its event.
            ReconcileFullText(fullTextBuffer,revision,start,end-start);
            Notify(Localization.Text("Edit one proposition at a time. Text across propositions and automatic separators cannot be replaced together."));return;
        }
        if(edit==null)return;
        editingFullText=true;
        try
        {
            var node=doc.Node(edit.NodeId)!;RememberEdit("full-text:"+node.Id);node.Title=edit.Body;
            int shift=fullTextBuffer.Commit(edit);Changed(false);fullTextBufferRevision=revision;
            ReconcileFullText(fullTextBuffer,revision,caretShift:shift);
            if(editorId!=node.Id)FocusTextObject(node.Id,false);
            bool was=updating;updating=true;
            try{SetText(PropositionBox,node.Title);PropositionBox.SetColors(node.BodyColors);}
            finally{updating=was;}
            RefreshFullTextCount();
        }
        finally{editingFullText=false;}
    }
    void ReconcileFullText(FullTextEditBuffer buffer,long expectedRevision,int? restoreStart=null,int restoreLength=0,int caretShift=0)
    {
        string edited=FullTextBox.Text;
        Dispatcher.BeginInvoke(()=>
        {
            if(fullTextBuffer!=buffer||revision!=expectedRevision||FullTextBox.Text!=edited||fullTextComposing)return;
            int start=restoreStart??FullTextBox.SelectionStart+caretShift;
            int length=restoreStart.HasValue?restoreLength:FullTextBox.SelectionLength;
            buildingFullText=true;
            try
            {
                var offset=FullTextBox.VerticalOffset;
                FullTextBox.Text=buffer.Text;FullTextBox.Select(start,length);
                FullTextBox.SetColors(buffer.Colors(doc));HighlightFullText(false);
                FullTextBox.ScrollToVerticalOffset(offset);RefreshFullTextCount();
                CopyFullTextButton.IsEnabled=buffer.Text.Length>0;
            }
            finally{buildingFullText=false;CaptureFullTextSelection();}
        },DispatcherPriority.Send);
    }
    internal void UndoFullText(bool forward)
    {
        int start=FullTextBox.SelectionStart,length=FullTextBox.SelectionLength;
        string? id=editorId,pageId=fullTextBuffer?.PageId;
        if(forward)Redo();else Undo();RefreshFullText();
        int page=CurrentFullText.Pages.ToList().FindIndex(p=>p.Id==pageId);
        if(page>=0&&page!=CurrentFullTextPageIndex)SelectFullTextPage(page);
        if(id!=null&&doc.Node(id)!=null)FocusTextObject(id,false);
        FullTextBox.Focus();FullTextBox.Select(start,length);
    }
    void UndoFocused(bool forward)
    {
        if(FullTextBox.IsKeyboardFocusWithin)UndoFullText(forward);
        else if(PropositionBox.IsKeyboardFocusWithin)UndoBody(forward);
        else if(forward)Redo();else Undo();
    }
}
