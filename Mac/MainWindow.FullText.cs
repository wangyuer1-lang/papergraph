using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
namespace Papergraph;
public partial class MainWindow
{
    readonly SelectableTextBlock manuscript=new(){FontSize=16,LineHeight=27,TextWrapping=TextWrapping.Wrap,Margin=new(2,6,8,6)};
    readonly ScrollViewer manuscriptScroll=new(){HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly TextBlock fullTextStatus=new(){FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    readonly TextBlock fullTextEmpty=new(){TextWrapping=TextWrapping.Wrap,Margin=new(8,14)};
    readonly StackPanel fullTextChecks=new(){Margin=new(21,0,12,7)};
    readonly ScrollViewer checksScroll=new(){MaxHeight=115,IsVisible=false};
    Button checksToggle=null!;bool checksExpanded;string checksLabel="";
    readonly StackPanel issueList=new(){Margin=new(14,5,0,3)};
    readonly Dictionary<string,Run> textRuns=[];
    readonly List<(int Start,int End,string Id)> textRanges=[];
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
        manuscriptScroll.Margin=new(18,0,12,10);manuscriptScroll.Content=manuscript;Grid.SetRow(manuscriptScroll,3);textArea.Children.Add(manuscriptScroll);Grid.SetRow(fullTextEmpty,3);fullTextEmpty.IsHitTestVisible=false;textArea.Children.Add(fullTextEmpty);
        manuscript.PropertyChanged+=(s,e)=>{if(e.Property==SelectableTextBlock.SelectionStartProperty||e.Property==SelectableTextBlock.SelectionEndProperty)UpdateTextStats();};
        manuscript.PointerReleased+=(s,e)=>{if(!string.IsNullOrEmpty(manuscript.SelectedText))return;var index=manuscript.TextLayout.HitTestPoint(e.GetPosition(manuscript)).TextPosition;var range=textRanges.FirstOrDefault(r=>index>=r.Start&&index<r.End);if(range.Id!=null)FocusTextObject(range.Id);};
        manuscript.ContextMenu=new ContextMenu{ItemsSource=new[]{MenuAction("Copy",manuscript.Copy),MenuAction("Select all",manuscript.SelectAll)}};
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
            var offset=manuscriptScroll.Offset;bool same=textDocument==file;if(!same){textAnchor=textAnchors.GetValueOrDefault(file);textDocument=file;}
            currentFullText=GraphFullText.Build(doc);var anchorIndex=currentFullText.Pages.ToList().FindIndex(p=>textAnchor!=null&&p.ObjectIds.Contains(textAnchor));if(anchorIndex>=0)fullTextPage=anchorIndex;else fullTextPage=Math.Clamp(fullTextPage,0,Math.Max(0,currentFullText.Pages.Count-1));
            textPages.ItemsSource=Enumerable.Range(1,currentFullText.Pages.Count).Select(i=>i.ToString()).ToArray();textPages.SelectedIndex=fullTextPage;textPages.IsVisible=currentFullText.Pages.Count>1;var page=currentFullText.Pages.ElementAtOrDefault(fullTextPage);
            textAnchor=page?.Pieces.FirstOrDefault()?.NodeId;if(textAnchor!=null)textAnchors[file]=textAnchor;
            manuscript.Inlines!.Clear();textRuns.Clear();textRanges.Clear();var position=0;
            foreach(var piece in page?.Pieces??[]){if(piece.Separator.Length>0){manuscript.Inlines.Add(new Run(piece.Separator));position+=piece.Separator.Length;}var run=new Run(piece.Text);manuscript.Inlines.Add(run);textRuns[piece.NodeId]=run;textRanges.Add((position,position+piece.Text.Length,piece.NodeId));position+=piece.Text.Length;}
            fullTextEmpty.IsVisible=page?.Pieces.Count is not >0;fullTextEmpty.Text=T(page?.BlockedPoints>0?"Reading order conflicts. Expand Checks to locate the connections.":"Connect points with Body arrows to see the text here.");
            fullTextStatus.Text=(currentFullText.Pages.Count>1?string.Format(T("Page {0}/{1} · "),fullTextPage+1,currentFullText.Pages.Count):"")+string.Format(T("{0} points · {1} excluded"),page?.Pieces.Count??0,currentFullText.ExcludedPoints);
            var issues=currentFullText.GeneralIssues.Concat(page?.Issues??[]).ToArray();checksLabel=string.Format(T("Checks · {0}"),issues.Length);checksToggle.Content=(checksExpanded?"▾ ":"▸ ")+checksLabel;fullTextChecks.IsVisible=issues.Length>0;issueList.Children.Clear();foreach(var issue in issues){var b=Button("",()=>{if(issue.ObjectId!=null)FocusTextObject(issue.ObjectId);});b.Content=new TextBlock{Text=issue.Message,FontSize=12,TextWrapping=TextWrapping.Wrap};b.Padding=new(3,4);b.HorizontalAlignment=HorizontalAlignment.Left;issueList.Children.Add(b);}
            copyTextButton.IsEnabled=page?.Text.Length>0;UpdateTextStats();HighlightFullText(false);manuscriptScroll.Offset=same?offset:default;
        }finally{buildingText=false;}
    }
    void SelectFullTextPage(int page){if(buildingText)return;fullTextPage=page;textAnchor=null;RefreshFullText();}
    void UpdateTextStats(){var page=currentFullText.Pages.ElementAtOrDefault(fullTextPage);var count=TextStatistics.Count(page?.Text??"");var words=count.Words.ToString("N0");var chars=count.Characters.ToString("N0");if(!string.IsNullOrEmpty(manuscript.SelectedText)){var part=TextStatistics.Count(manuscript.SelectedText);words=part.Words.ToString("N0")+" / "+words;chars=part.Characters.ToString("N0")+" / "+chars;}textStats.Text=string.Format(T("Words: {0} · Characters: {1}"),words,chars);}
    void HighlightFullText(bool reveal=true)
    {
        if(!fullTextVisible)return;var index=currentFullText.Pages.ToList().FindIndex(p=>editorId!=null&&(p.ObjectIds.Contains(editorId)||p.EdgeIds.Contains(editorId)));if(reveal&&!buildingText&&index>=0&&index!=fullTextPage){SelectFullTextPage(index);return;}foreach(var (id,run) in textRuns)run.Background=id==editorId?Ui("HoverBrush"):Brushes.Transparent;
    }
    async Task CopyFullText(){try{var text=currentFullText.Pages.ElementAtOrDefault(fullTextPage)?.Text;if(text!=null&&GetTopLevel(this)?.Clipboard is {} clipboard){await clipboard.SetTextAsync(text);Notify("Page text copied");}}catch(Exception ex){Notify(ex.Message);}}
    void FocusTextObject(string id)
    {
        if(!doc.HasEndpoint(id)&&!doc.Edges.Any(e=>e.Id==id))return;if(Graph.Scope!=null||Graph.BoardRegion!=null){boards.Push(CaptureBoard());Graph.SetBoard(null,null);}Graph.ClearAllSelection();if(doc.Node(id)!=null)Graph.Selected=[id];else if(doc.Frame(id)!=null)Graph.SelectedRegion=id;else Graph.SelectedEdge=id;SelectionChanged();var endpoint=doc.Edges.FirstOrDefault(e=>e.Id==id)?.From??id;if(doc.Node(endpoint)!=null)Graph.EnsureVisible(endpoint);else if(doc.Frame(endpoint) is Region frame)Graph.SetView(Graph.Zoom,new System.Windows.Point(Graph.ActualWidth/2-(frame.X+frame.Width/2)*Graph.Zoom,Graph.ActualHeight/2-(frame.Y+frame.Height/2)*Graph.Zoom));Graph.Focus();
    }
}
