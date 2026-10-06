using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Papergraph;

public partial class MainWindow
{
    bool fullTextVisible;
    readonly DispatcherTimer fullTextTimer=new(){Interval=TimeSpan.FromMilliseconds(180)};
    readonly Dictionary<string,Run> fullTextRuns=[];
    string? highlightedTextId;
    Button? fullTextButton;
    readonly Dictionary<string,string> fullTextPageAnchors=new(StringComparer.OrdinalIgnoreCase);
    string? fullTextDocument;
    string? fullTextAnchor;
    TextStatistics fullTextPageStatistics;
    internal FullTextResult CurrentFullText {get;private set;}=new("",[],[],0,0,0);
    internal int CurrentFullTextPageIndex {get;private set;}
    internal FullTextPage? CurrentFullTextPage=>CurrentFullText.Pages.ElementAtOrDefault(CurrentFullTextPageIndex);

    void InitializeFullText()
    {
        fullTextButton=new Button{Content=Localization.Text("Full text"),Width=82,ToolTip=Localization.Text("Show or hide live full text"),Margin=new Thickness(2,0,0,0)};
        AutomationProperties.SetName(fullTextButton,"Show live full text");
        ((StackPanel)FitButton.Parent).Children.Insert(((StackPanel)FitButton.Parent).Children.IndexOf(FitButton),fullTextButton);
        fullTextButton.Click+=(s,e)=>SetFullTextVisible(!fullTextVisible);
        var menu=LocalizedItem(ViewMenu,"Live full text",()=>SetFullTextVisible(!fullTextVisible));menu.IsCheckable=true;
        ViewMenu.SubmenuOpened+=(s,e)=>menu.IsChecked=fullTextVisible;
        fullTextTimer.Tick+=(s,e)=>{fullTextTimer.Stop();RefreshFullText();};
        CopyFullTextButton.Click+=(s,e)=>
        {
            RefreshFullText();var text=CurrentFullTextPage?.Text;if(string.IsNullOrEmpty(text))return;
            try{Clipboard.SetText(text);Notify(Localization.Text("Page text copied"));}catch(Exception ex){Notify(Localization.Text("Could not copy: ")+ex.Message);}
        };
        FullTextBox.PreviewMouseLeftButtonUp+=(s,e)=>
        {
            if(!FullTextBox.Selection.IsEmpty)return;
            var position=FullTextBox.GetPositionFromPoint(e.GetPosition(FullTextBox),true);
            DependencyObject? parent=position?.Parent;
            while(parent is not null and not Run)parent=parent is FrameworkContentElement content?content.Parent:null;
            if(parent is Run {Tag:string id})FocusTextObject(id);
        };
        FullTextBox.SelectionChanged+=(s,e)=>RefreshFullTextCount();
        var copyMenu=Menu(FullTextBox,PlacementMode.MousePoint);
        copyMenu.Items.Add(new MenuItem{Header=Localization.Text("Copy"),Command=ApplicationCommands.Copy,CommandTarget=FullTextBox});
        copyMenu.Items.Add(new MenuItem{Header=Localization.Text("Select all"),Command=ApplicationCommands.SelectAll,CommandTarget=FullTextBox});foreach(var item in copyMenu.Items.OfType<MenuItem>())Localization.Bind(item,HeaderedItemsControl.HeaderProperty,item.Command==ApplicationCommands.Copy?"Copy":"Select all");FullTextBox.ContextMenu=copyMenu;
        TextOrderBox.TextChanged+=(s,e)=>
        {
            if(updating||editorKind!="edge"||editorId==null)return;
            var text=TextOrderBox.Text.Trim();
            if(text.Length>0&&(!int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var number)||number>9999))return;
            var order=text.Length==0?0:int.Parse(text,CultureInfo.InvariantCulture);
            var edge=doc.Edges.FirstOrDefault(x=>x.Id==editorId);if(edge==null||edge.TextOrder==order)return;
            RememberEdit("text-order:"+edge.Id);edge.TextOrder=order;Changed(false);
        };
        Closed+=(s,e)=>fullTextTimer.Stop();
        SetFullTextVisible(fullTextVisible,false);
    }
    internal void SetFullTextVisible(bool visible,bool persist=true)
    {
        fullTextVisible=visible;FullTextPanel.Visibility=FullTextDivider.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        FullTextDividerRow.Height=new GridLength(visible?4:0);FullTextRow.Height=new GridLength(visible?300:0);
        FullTextRow.MinHeight=visible?150:0;
        if(fullTextButton!=null)fullTextButton.Background=visible?Ui("HoverBrush"):Brushes.Transparent;
        if(visible)RefreshFullText();else fullTextTimer.Stop();
        if(persist)SaveViewSettings();
    }
    void QueueFullText()
    {
        if(!fullTextVisible)return;fullTextTimer.Stop();fullTextTimer.Start();
    }
    internal void RefreshFullText()
    {
        if(!fullTextVisible)return;
        bool sameDocument=string.Equals(fullTextDocument,file,StringComparison.OrdinalIgnoreCase);
        var previous=sameDocument?CurrentFullTextPage:null;
        if(!sameDocument){fullTextDocument=file;fullTextAnchor=fullTextPageAnchors.GetValueOrDefault(file);}
        CurrentFullText=GraphFullText.Build(doc);
        var index=FindFullTextPage(fullTextAnchor);
        if(index<0&&previous!=null)
            index=CurrentFullText.Pages.Select((p,i)=>(Page:p,Index:i)).FirstOrDefault(p=>p.Page.ObjectIds.Intersect(previous.ObjectIds).Any(),(null!,-1)).Index;
        CurrentFullTextPageIndex=Math.Max(0,index);
        RenderFullTextPage(sameDocument&&previous!=null&&CurrentFullTextPage?.ObjectIds.Intersect(previous.ObjectIds).Any()==true);
    }
    int FindFullTextPage(string? id)=>id==null?-1:CurrentFullText.Pages.ToList().FindIndex(p=>p.ObjectIds.Contains(id)||p.EdgeIds.Contains(id));
    internal void SelectFullTextPage(int index)
    {
        if(index<0||index>=CurrentFullText.Pages.Count||index==CurrentFullTextPageIndex)return;
        CurrentFullTextPageIndex=index;fullTextAnchor=null;RenderFullTextPage(false);
    }
    void RenderFullTextPage(bool preserveScroll)
    {
        var page=CurrentFullTextPage;var offset=preserveScroll?FullTextBox.VerticalOffset:0;
        fullTextPageStatistics=TextStatistics.Count(page?.Text??"");
        fullTextRuns.Clear();highlightedTextId=null;
        if(page!=null)
        {
            if(fullTextAnchor==null||!page.ObjectIds.Contains(fullTextAnchor))fullTextAnchor=page.Pieces.FirstOrDefault()?.NodeId??page.ObjectIds.FirstOrDefault();
            if(fullTextAnchor!=null)fullTextPageAnchors[file]=fullTextAnchor;
        }
        FullTextPageTabs.Children.Clear();
        FullTextPagesScroller.Visibility=CurrentFullText.Pages.Count>1?Visibility.Visible:Visibility.Collapsed;
        for(int i=0;i<CurrentFullText.Pages.Count;i++)
        {
            var index=i;var number=(i+1).ToString(CultureInfo.InvariantCulture);
            var tab=new RadioButton{Content=number,MinWidth=28,Width=double.NaN,GroupName="FullTextPages",Style=(Style)FindResource("NotePageTab"),
                IsChecked=i==CurrentFullTextPageIndex,ToolTip=Localization.Format("Page {0} · {1} points",number,CurrentFullText.Pages[i].Pieces.Count)};
            AutomationProperties.SetName(tab,"Full text page "+number);
            tab.Checked+=(s,e)=>SelectFullTextPage(index);FullTextPageTabs.Children.Add(tab);
        }
        var flow=new FlowDocument{FontFamily=FontFamily,FontSize=16,PagePadding=new Thickness(0),LineHeight=27};
        flow.SetResourceReference(FlowDocument.ForegroundProperty,"TextBrush");
        Paragraph? paragraph=null;
        foreach(var piece in page?.Pieces??[])
        {
            if(paragraph==null||piece.Separator.Contains("\n\n"))
            {
                paragraph=new Paragraph{Margin=new Thickness(0,0,0,18)};flow.Blocks.Add(paragraph);
            }
            else if(piece.Separator.Contains('\n'))paragraph.Inlines.Add(new LineBreak());
            else if(piece.Separator.Length>0)paragraph.Inlines.Add(new Run(piece.Separator));
            var node=doc.Node(piece.NodeId)!;
            var run=new Run(piece.Text){Tag=piece.NodeId,Cursor=Cursors.Hand,ToolTip=(node.Caption.Length>0?node.Caption+" · ":"")+"Click to locate the source point"};
            paragraph.Inlines.Add(run);fullTextRuns[piece.NodeId]=run;
        }
        FullTextBox.Document=flow;FullTextBox.ScrollToVerticalOffset(offset);
        RefreshFullTextCount();
        FullTextEmpty.Visibility=page?.Pieces.Count>0?Visibility.Collapsed:Visibility.Visible;
        FullTextEmpty.Text=Localization.Text(page?.BlockedPoints>0?"Reading order conflicts. Expand Checks to locate the connections.":"Connect points with Body arrows to see the text here.");
        FullTextStatus.Text=(CurrentFullText.Pages.Count>1?Localization.Format("Page {0}/{1} · ",CurrentFullTextPageIndex+1,CurrentFullText.Pages.Count):"")+Localization.Format("{0} points · {1} excluded",page?.Pieces.Count??0,CurrentFullText.ExcludedPoints);
        FullTextStatus.ToolTip="Points and blocked counts refer to this page. Excluded counts refer to the whole graph: isolated points and points connected only by reference or bidirectional arrows. Notes and container descriptions are not included.";
        if(page?.BlockedPoints>0)FullTextStatus.Text+=$" · {page.BlockedPoints} blocked";
        var issues=CurrentFullText.GeneralIssues.Concat(page?.Issues??[]).ToArray();
        FullTextChecks.Header=Localization.Format("Checks · {0}",issues.Length);
        FullTextChecks.Visibility=issues.Length==0?Visibility.Collapsed:Visibility.Visible;
        FullTextIssueList.Children.Clear();
        foreach(var issue in issues)
        {
            var button=new Button{Content=new TextBlock{Text=issue.Message,TextWrapping=TextWrapping.Wrap,FontSize=12},HorizontalAlignment=HorizontalAlignment.Left,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(3,4,3,4),ToolTip=Localization.Text("Click to locate the object to check")};
            var id=issue.ObjectId;button.Click+=(s,e)=>{if(id!=null)FocusTextObject(id);};FullTextIssueList.Children.Add(button);
        }
        CopyFullTextButton.IsEnabled=page?.Text.Length>0;
        HighlightFullText(false);
    }
    void RefreshFullTextCount()
    {
        var total=fullTextPageStatistics;
        string words=total.Words.ToString("N0",CultureInfo.InvariantCulture),characters=total.Characters.ToString("N0",CultureInfo.InvariantCulture);
        bool selected=!FullTextBox.Selection.IsEmpty;
        if(selected)
        {
            var part=TextStatistics.Count(FullTextBox.Selection.Text);
            words=part.Words.ToString("N0",CultureInfo.InvariantCulture)+" / "+words;
            characters=part.Characters.ToString("N0",CultureInfo.InvariantCulture)+" / "+characters;
        }
        FullTextCount.Text=Localization.Format("Words: {0} · Characters: {1}",words,characters);
        FullTextCount.ToolTip=(selected?"Selection / current page. ":"Current page. ")+
            "Words: Chinese characters and Japanese kana count individually; other letters and numbers are grouped into words. Punctuation is excluded from words. " +
            "Characters: includes punctuation; excludes spaces, tabs and line breaks. Notes and excluded points are not counted.";
        AutomationProperties.SetName(FullTextCount,(selected?"Selected text / current page. ":"Current page. ")+FullTextCount.Text);
    }
    void HighlightFullText(bool reveal=true)
    {
        if(reveal&&fullTextVisible)
        {
            var index=FindFullTextPage(editorId);
            if(index>=0&&index!=CurrentFullTextPageIndex){SelectFullTextPage(index);if(editorId!=null&&fullTextRuns.TryGetValue(editorId,out var selectedRun))selectedRun.BringIntoView();return;}
        }
        var selected=editorKind=="node"?editorId:null;if(highlightedTextId==selected)return;
        if(highlightedTextId!=null&&fullTextRuns.TryGetValue(highlightedTextId,out var previous))previous.Background=Brushes.Transparent;
        highlightedTextId=selected;
        if(selected!=null&&fullTextRuns.TryGetValue(selected,out var run))
        {run.SetResourceReference(TextElement.BackgroundProperty,"HoverBrush");if(reveal&&fullTextVisible)run.BringIntoView();}
    }
    internal void FocusTextObject(string id)
    {
        if(!doc.HasEndpoint(id)&&!doc.Edges.Any(e=>e.Id==id))return;
        if(Graph.Scope!=null||Graph.BoardRegion!=null){boards.Push(CaptureBoard());Graph.SetBoard(null,null);BackButton.Visibility=Visibility.Collapsed;}
        Graph.ClearAllSelection();
        if(doc.Node(id)!=null)Graph.Selected=[id];else if(doc.Frame(id)!=null)Graph.SelectedRegion=id;else Graph.SelectedEdge=id;
        SelectionChanged();
        var node=doc.Node(id);
        if(node!=null)Graph.EnsureVisible(id);
        else
        {
            var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);var endpoint=edge?.From??id;
            if(doc.Node(endpoint)!=null)Graph.EnsureVisible(endpoint);
            else if(doc.Frame(endpoint) is Region frame)
            {
                var center=new Point(frame.X+frame.Width/2,frame.Y+frame.Height/2);
                Graph.SetView(Graph.Zoom,new Point(Graph.ActualWidth/2-center.X*Graph.Zoom,Graph.ActualHeight/2-center.Y*Graph.Zoom));
            }
        }
        ensurePending=false;Graph.Focus();
    }
    void BuildTextRoleChoices(Relation edge)
    {
        TextRoleChoices.Children.Clear();
        foreach(var (role,label,tip) in new[]{("flow","Body","Sets the reading order of the body text"),("reference","Reference","A reference or association; does not set the reading order")})
        {
            var button=new Button{Content=Localization.Text(label),ToolTip=Localization.Text(tip),Margin=new Thickness(2),Background=(edge.TextRole??"flow")==role?Ui("HoverBrush"):Brushes.Transparent};
            AutomationProperties.SetName(button,Localization.Text("Text role: ")+Localization.Text(label));button.Click+=(s,e)=>SetTextRole(edge.Id,role);TextRoleChoices.Children.Add(button);
        }
        TextOrderPanel.Visibility=GraphFullText.IsReference(edge)?Visibility.Collapsed:Visibility.Visible;
        SetText(TextOrderBox,edge.TextOrder==0?"":edge.TextOrder.ToString(CultureInfo.InvariantCulture));
    }
    internal void SetTextRole(string id,string role)
    {
        var edge=doc.Edges.FirstOrDefault(e=>e.Id==id);if(edge==null||(edge.TextRole??"flow")==role)return;
        if(role is not ("flow" or "reference"))throw new ArgumentException("Invalid text role");
        Remember();edge.TextRole=role=="flow"?null:role;Changed();
    }
}
