using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Papergraph;
public partial class MainWindow
{
    readonly BodyTextBox manuscript=new(){FontSize=16,LineHeight=27,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,IsUndoEnabled=false,BorderThickness=new(0),Padding=new(2,6,8,6)};
    ScrollViewer? ManuscriptScroll=>manuscript.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    readonly TextBlock fullTextStatus=new(){FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock fullTextEmpty=new(){TextWrapping=TextWrapping.Wrap,Margin=new(8,14)};
    readonly StackPanel fullTextChecks=new(){Margin=new(21,0,12,7)};
    readonly ScrollViewer checksScroll=new(){MaxHeight=115,IsVisible=false};
    Button checksToggle=null!;bool checksExpanded;string checksLabel="";
    readonly StackPanel issueList=new(){Margin=new(14,5,0,3)};
    FullTextEditBuffer? fullTextBuffer;
    long fullTextBufferRevision=-1;
    int manuscriptSelectionStart,manuscriptSelectionEnd;
    readonly Dictionary<string,string> textAnchors=[];
    string? textDocument,textAnchor;
    FullTextResult currentFullText=new("",[],[],0,0,0);
    Button copyTextButton=null!;
    void BuildFullTextUi()
    {
        textArea.RowDefinitions=new("Auto,Auto,Auto,*");textArea.Margin=new(0);Paint(()=>{textArea.Background=Ui("SurfaceBrush");fullTextStatus.Foreground=fullTextEmpty.Foreground=Ui("MutedBrush");manuscript.Foreground=Ui("TextBrush");manuscript.SelectionBrush=Ui("HoverBrush");});
        var head=new DockPanel{Margin=new(22,8,12,4)};textArea.Children.Add(head);copyTextButton=Button("Copy",()=>_=CopyFullText());DockPanel.SetDock(copyTextButton,Dock.Right);head.Children.Add(copyTextButton);textStats.FontSize=12;textStats.VerticalAlignment=VerticalAlignment.Center;textStats.Margin=new(16,0,20,0);DockPanel.SetDock(textStats,Dock.Right);head.Children.Add(textStats);
        var label=Label("Full text");label.FontWeight=FontWeight.Normal;label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new(0,0,16,0);DockPanel.SetDock(label,Dock.Left);head.Children.Add(label);head.Children.Add(fullTextStatus);
        Grid.SetRow(textPages,1);textPages.Margin=new(21,0,12,4);textArea.Children.Add(textPages);checksToggle=Button("",()=>{checksExpanded=!checksExpanded;checksScroll.IsVisible=checksExpanded;checksToggle.Content=(checksExpanded?"▾ ":"▸ ")+checksLabel;});checksToggle.FontSize=12;checksToggle.Padding=new(3,2);checksToggle.HorizontalAlignment=HorizontalAlignment.Left;checksToggle.MinHeight=22;fullTextChecks.Children.Add(checksToggle);checksScroll.Content=issueList;fullTextChecks.Children.Add(checksScroll);Grid.SetRow(fullTextChecks,2);textArea.Children.Add(fullTextChecks);
        manuscript.Classes.Add("proposition-colors");manuscript.Margin=new(18,0,12,10);Grid.SetRow(manuscript,3);textArea.Children.Add(manuscript);Grid.SetRow(fullTextEmpty,3);fullTextEmpty.IsHitTestVisible=false;textArea.Children.Add(fullTextEmpty);
        Avalonia.Automation.AutomationProperties.SetName(manuscript,"Full text editor");
        Tip(manuscript,"Click to edit. Changes are saved to the source proposition.");
        manuscript.PropertyChanged+=(s,e)=>{
            if(e.Property==TextBox.TextProperty){if(!buildingText)EditFullText();}
            else if(e.Property==TextBox.SelectionStartProperty||e.Property==TextBox.SelectionEndProperty){
                manuscriptSelectionStart=manuscript.SelectionStart;manuscriptSelectionEnd=manuscript.SelectionEnd;UpdateTextStats();
            }
        };
        manuscript.GotFocus+=(s,e)=>{if(fullTextBufferRevision!=revision)RefreshFullText();};
        manuscript.LostFocus+=(s,e)=>editKey="";
        manuscript.PointerReleased+=(s,e)=>{
            if(!string.IsNullOrEmpty(manuscript.SelectedText))return;
            var range=fullTextBuffer?.At(manuscript.CaretIndex);
            if(range!=null)FocusTextObject(range.NodeId,false);
        };
        ConfigureTextMenu(manuscript);
    }
    void ToggleFullText()=>SetFullTextVisible(!fullTextVisible);
    void SetFullTextVisible(bool visible,bool persist=true)
    {
        fullTextVisible=visible;textArea.IsVisible=fullTextDivider.IsVisible=visible;workspace.RowDefinitions[1].Height=new GridLength(visible?4:0);workspace.RowDefinitions[2].Height=new GridLength(visible?300:0);workspace.RowDefinitions[2].MinHeight=visible?150:0;fullTextButton.Classes.Set("selected",visible);if(visible)RefreshFullText();else textTimer.Stop();if(persist)SaveSettings();
    }
    void RefreshFullText(bool force=false)
    {
        if(!fullTextVisible||buildingText)return;buildingText=true;
        try
        {
            var offset=ManuscriptScroll?.Offset??default;bool same=textDocument==file;if(!same){textAnchor=textAnchors.GetValueOrDefault(file);textDocument=file;fullTextBuffer=null;}
            currentFullText=GraphFullText.Build(doc);var anchorIndex=currentFullText.Pages.ToList().FindIndex(p=>textAnchor!=null&&p.ObjectIds.Contains(textAnchor));if(anchorIndex>=0)fullTextPage=anchorIndex;else fullTextPage=Math.Clamp(fullTextPage,0,Math.Max(0,currentFullText.Pages.Count-1));
            textPages.ItemsSource=Enumerable.Range(1,currentFullText.Pages.Count).Select(i=>i.ToString()).ToArray();textPages.SelectedIndex=fullTextPage;textPages.IsVisible=currentFullText.Pages.Count>1;var page=currentFullText.Pages.ElementAtOrDefault(fullTextPage);
            textAnchor=page?.Pieces.FirstOrDefault()?.NodeId;if(textAnchor!=null)textAnchors[file]=textAnchor;
            bool preserve=fullTextBuffer!=null&&fullTextBuffer.PageId==page?.Id&&fullTextBufferRevision==revision&&fullTextBuffer.Matches(doc);
            if(!preserve)fullTextBuffer=page==null?null:new FullTextEditBuffer(doc,page);
            fullTextBufferRevision=revision;
            int start=manuscript.SelectionStart,end=manuscript.SelectionEnd;
            if(manuscript.Text!=(fullTextBuffer?.Text??"")){
                manuscript.Text=fullTextBuffer?.Text??"";manuscript.SelectionStart=same?Math.Min(start,manuscript.Text.Length):0;manuscript.SelectionEnd=same?Math.Min(end,manuscript.Text.Length):0;
            }
            manuscript.SetColors(fullTextBuffer?.Colors(doc));manuscript.IsReadOnly=fullTextBuffer?.Spans.Count is not >0;
            fullTextEmpty.IsVisible=manuscript.IsReadOnly;fullTextEmpty.Text=T(page?.BlockedPoints>0?"Reading order conflicts. Expand Checks to locate the connections.":"Connect points with Body arrows to see the text here.");
            fullTextStatus.Text=(currentFullText.Pages.Count>1?string.Format(T("Page {0}/{1} · "),fullTextPage+1,currentFullText.Pages.Count):"")+string.Format(T("{0} points · {1} excluded"),page?.Pieces.Count??0,currentFullText.ExcludedPoints);
            var issues=currentFullText.GeneralIssues.Concat(page?.Issues??[]).ToArray();checksLabel=string.Format(T("Checks · {0}"),issues.Length);checksToggle.Content=(checksExpanded?"▾ ":"▸ ")+checksLabel;fullTextChecks.IsVisible=issues.Length>0;issueList.Children.Clear();foreach(var issue in issues){var b=Button("",()=>{if(issue.ObjectId!=null)FocusTextObject(issue.ObjectId);});b.Content=new TextBlock{Text=issue.Message,FontSize=12,TextWrapping=TextWrapping.Wrap};b.Padding=new(3,4);b.HorizontalAlignment=HorizontalAlignment.Left;issueList.Children.Add(b);}
            copyTextButton.IsEnabled=manuscript.Text?.Length>0;UpdateTextStats();HighlightFullText(false);if(ManuscriptScroll is {} scroll)scroll.Offset=same?offset:default;
        }finally{buildingText=false;}
    }
    void SelectFullTextPage(int page){if(buildingText)return;fullTextPage=page;textAnchor=null;fullTextBuffer=null;editKey="";RefreshFullText();}
    void UpdateTextStats(){var count=TextStatistics.Count(manuscript.Text??"");var words=count.Words.ToString("N0");var chars=count.Characters.ToString("N0");if(!string.IsNullOrEmpty(manuscript.SelectedText)){var part=TextStatistics.Count(manuscript.SelectedText);words=part.Words.ToString("N0")+" / "+words;chars=part.Characters.ToString("N0")+" / "+chars;}textStats.Text=string.Format(T("Words: {0} · Characters: {1}"),words,chars);}
    void HighlightFullText(bool reveal=true)
    {
        if(!fullTextVisible)return;bool onCurrentPage=fullTextBuffer?.Spans.Any(p=>p.NodeId==editorId)==true;
        var index=currentFullText.Pages.ToList().FindIndex(p=>editorId!=null&&(p.ObjectIds.Contains(editorId)||p.EdgeIds.Contains(editorId)||p.Pieces.Any(x=>x.References.Any(r=>r.NodeId==editorId||r.EdgeId==editorId))));if(reveal&&!buildingText&&!onCurrentPage&&index>=0&&index!=fullTextPage){SelectFullTextPage(index);return;}
        var range=fullTextBuffer?.Spans.FirstOrDefault(p=>p.NodeId==editorId);manuscript.SetHighlight(range?.Start??0,range==null?0:range.End-range.Start,Ui("HoverBrush"));
        manuscript.SetBackgrounds(fullTextBuffer?.ReferenceHighlights(dark));
    }
    void EditFullText()
    {
        if(fullTextBuffer==null)return;
        if(textDocument!=file||fullTextBufferRevision!=revision||!fullTextBuffer.Matches(doc)){
            RefreshFullText();Notify("The source changed. Review the refreshed text and try again.");return;
        }
        int start=manuscriptSelectionStart,end=manuscriptSelectionEnd;
        if(!fullTextBuffer.TryPrepare(manuscript.Text??"",start,end,editorId,out var edit)){
            buildingText=true;
            try{manuscript.Text=fullTextBuffer.Text;manuscript.SelectionStart=start;manuscript.SelectionEnd=end;}
            finally{buildingText=false;}
            Notify("Edit one proposition at a time. Text across propositions and automatic separators cannot be replaced together.");
            return;
        }
        if(edit==null)return;
        var node=doc.Node(edit.NodeId)!;RememberEdit("full-text:"+edit.NodeId);node.Title=edit.Body;int caretShift=fullTextBuffer.Commit(edit);Changed(false);fullTextBufferRevision=revision;
        if(manuscript.Text!=fullTextBuffer.Text)
        {
            // The same reference may appear more than once. Let the native input
            // event finish before reconciling its other appearances and caret.
            var buffer=fullTextBuffer;var expectedRevision=revision;var edited=manuscript.Text;
            Dispatcher.UIThread.Post(()=>{
                if(fullTextBuffer!=buffer||revision!=expectedRevision||manuscript.Text!=edited)return;
                int a=manuscript.SelectionStart+caretShift,b=manuscript.SelectionEnd+caretShift;
                buildingText=true;
                try{manuscript.Text=buffer.Text;manuscript.SelectionStart=Math.Clamp(a,0,buffer.Text.Length);manuscript.SelectionEnd=Math.Clamp(b,0,buffer.Text.Length);}
                finally{buildingText=false;}
                manuscript.SetColors(buffer.Colors(doc));HighlightFullText(false);UpdateTextStats();
            },DispatcherPriority.Send);
        }
        if(editorId!=node.Id)FocusTextObject(node.Id,false);
        // Update the other editor without moving focus or replaying its edit handler.
        if(editorId==node.Id){
            bool was=updating;updating=true;
            try{SetEditorText(bodyBox,node.Title);bodyBox.SetColors(node.BodyColors);}
            finally{updating=was;}
        }
        manuscript.SetColors(fullTextBuffer.Colors(doc));HighlightFullText(false);UpdateTextStats();
    }
    void UndoFullText(bool forward)
    {
        int start=manuscript.SelectionStart,end=manuscript.SelectionEnd;string? id=editorId,pageId=fullTextBuffer?.PageId;
        if(forward)Redo();else Undo();RefreshFullText();
        int page=currentFullText.Pages.ToList().FindIndex(p=>p.Id==pageId);if(page>=0&&page!=fullTextPage)SelectFullTextPage(page);
        if(id!=null&&doc.Node(id)!=null)FocusTextObject(id,false);
        manuscript.Focus();manuscript.SelectionStart=Math.Min(start,manuscript.Text?.Length??0);manuscript.SelectionEnd=Math.Min(end,manuscript.Text?.Length??0);
    }
    void UndoFocused(bool forward){if(manuscript.IsKeyboardFocusWithin)UndoFullText(forward);else if(bodyBox.IsKeyboardFocusWithin)UndoBody(forward);else if(forward)Redo();else Undo();}
    async Task CopyFullText(){try{var text=manuscript.Text;if(text!=null&&GetTopLevel(this)?.Clipboard is {} clipboard){await clipboard.SetTextAsync(text);Notify("Page text copied");}}catch(Exception ex){Notify(ex.Message);}}
    void FocusTextObject(string id,bool focusCanvas=true)
    {
        if(!doc.HasEndpoint(id)&&!doc.Edges.Any(e=>e.Id==id))return;if(Graph.Scope!=null||Graph.BoardRegion!=null){boards.Push(CaptureBoard());Graph.SetBoard(null,null);}Graph.ClearAllSelection();if(doc.Node(id)!=null)Graph.Selected=[id];else if(doc.Frame(id)!=null)Graph.SelectedRegion=id;else Graph.SelectedEdge=id;SelectionChanged();var endpoint=doc.Edges.FirstOrDefault(e=>e.Id==id)?.From??id;if(doc.Node(endpoint)!=null)Graph.EnsureVisible(endpoint);else if(doc.Frame(endpoint) is Region frame)Graph.SetView(Graph.Zoom,new System.Windows.Point(Graph.ActualWidth/2-(frame.X+frame.Width/2)*Graph.Zoom,Graph.ActualHeight/2-(frame.Y+frame.Height/2)*Graph.Zoom));if(focusCanvas)Graph.Focus();
    }
}
