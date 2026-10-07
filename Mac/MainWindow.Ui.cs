using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
namespace Papergraph;
public partial class MainWindow
{
    readonly Grid workspace=new(){RowDefinitions=new("*,0,0")};
    readonly Grid editorLayout=new(){RowDefinitions=new("40,2*,*"),Margin=new(21,14,18,20)};
    readonly Border captionPanel=new(){Margin=new(0,6,0,3),Padding=new(0,0,0,5),BorderThickness=new(0,0,0,1)};
    readonly Border notesPanel=new(){BorderThickness=new(0,1,0,0)};
    readonly TextBlock emptyEditor=new(){FontSize=13,Margin=new(7,24,0,0)};
    readonly Border multiBar=new(){CornerRadius=new(10),Padding=new(5),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(16,0,16,22),IsVisible=false};
    readonly Border toast=new(){CornerRadius=new(9),Padding=new(14,9),BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,Margin=new(24,0,24,24),IsVisible=false};
    readonly Avalonia.Threading.DispatcherTimer toastTimer=new(){Interval=TimeSpan.FromSeconds(4)};
    readonly GridSplitter fullTextDivider=new(){Height=4,HorizontalAlignment=HorizontalAlignment.Stretch,ResizeDirection=GridResizeDirection.Rows,IsVisible=false};
    readonly Avalonia.Controls.Shapes.Ellipse saveIndicator=new(){Width=5,Height=5,Margin=new(10,0,15,0)};
    readonly ObjectGlyph nodeGlyph=new(){Kind="point",Width=20,Height=20};
    readonly ObjectGlyph editorSymbol=new(){Kind="frame",Width=20,Height=20,Margin=new(5,0,0,0)};
    Button toastUndo=null!,backButton=null!,themeButton=null!,connectionButton=null!,fullTextButton=null!,colorButton=null!,linkButton=null!,moreButton=null!,frameButton=null!,circleButton=null!,deleteButton=null!;
    readonly Menu mainMenu=new();
    readonly TextBlock edgeLabel=new(){FontSize=18,Margin=new(0,0,0,15)};
    readonly UniformGrid directionButtons=new(){Columns=3,Margin=new(0,0,0,15)},relationButtons=new(){Columns=3},roleButtons=new(){Columns=2,Margin=new(0,0,0,8)};
    readonly DockPanel orderPanel=new(){Margin=new(0,0,0,18)};
    readonly TextBox textOrder=new(){Width=62,Margin=new(12,0,0,0)};
    readonly List<Action> themeBindings=[];
    IBrush Ui(string key)=>(IBrush)Resources[key]!;
    void Paint(Action action){themeBindings.Add(action);}
    Border Surface(Control content,Thickness border=default,Thickness padding=default)
    {
        var panel=new Border{Child=content,BorderThickness=border,Padding=padding};Paint(()=>{panel.Background=Ui("SurfaceBrush");panel.BorderBrush=Ui("BorderBrush");});return panel;
    }
    Button GlyphButton(string symbol,string tip,Action action,double width=39,double font=21)
    {
        var button=Button("",action);button.Width=width;button.FontSize=font;button.Padding=new(0);
        var glyph=new ToolbarGlyph { Symbol=symbol,Width=symbol=="▣＋"?font*1.5:font,Height=font,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
        button.Content=glyph;Paint(()=>{glyph.Ink=Ui("MutedBrush");glyph.InvalidateVisual();});Tip(button,tip);return button;
    }
    void Tip(Control control,string text){AutomationProperties.SetName(control,T(text));ToolTip.SetTip(control,T(text));if(collectLabels)localize.Add(()=>{AutomationProperties.SetName(control,T(text));ToolTip.SetTip(control,T(text));});}
    void BuildUi()
    {
        var root=new Grid{RowDefinitions=new("46,*")};Content=root;
        var toolbar=new DockPanel{LastChildFill=true};root.Children.Add(Surface(toolbar,new(0,0,0,1),new(9,2)));
        mainMenu.ItemContainerTheme=(ControlTheme)Application.Current!.FindResource("HorizontalMenuItem")!;
        mainMenu.VerticalAlignment=VerticalAlignment.Center;mainMenu.Margin=new(0,0,20,0);DockPanel.SetDock(mainMenu,Dock.Left);toolbar.Children.Add(mainMenu);BuildMenus();
        var commands=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(commands,Dock.Right);toolbar.Children.Add(commands);
        commands.Children.Add(saveIndicator);themeButton=GlyphButton("☾","Switch theme",()=>{dark=!dark;ApplyTheme();SaveSettings();},font:ThemeSymbols.Size);commands.Children.Add(themeButton);
        commands.Children.Add(GlyphButton("＋","New proposition · or double-click the canvas",()=>AddNode(FreePosition()),39,22));
        connectionButton=Button("Links: All",()=>SetConnectionDisplay((ConnectionDisplay)(((int)Graph.ConnectionDisplay+1)%3)));connectionButton.Width=112;connectionButton.Margin=new(4,0,0,0);commands.Children.Add(connectionButton);
        var arrange=Button("Arrange",()=>{Graph.Focus();Graph.ArrangeNaturally();});arrange.Width=76;Tip(arrange,"Arrange inside the selected □ or ◎; otherwise arrange each □ separately. Keeps frames fixed. Ctrl+Z to undo.");commands.Children.Add(arrange);
        fullTextButton=Button("Full text",ToggleFullText);fullTextButton.Width=82;fullTextButton.Margin=new(2,0,0,0);Tip(fullTextButton,"Show or hide live full text");commands.Children.Add(fullTextButton);
        var fit=Button("Fit all",()=>Graph.Fit());fit.MinWidth=66;fit.Margin=new(4,0,0,0);Tip(fit,"Show all objects on this board · F");commands.Children.Add(fit);
        var fitEdit=Button("Fit edit",()=>Graph.Fit(editing:true));fitEdit.MinWidth=72;Tip(fitEdit,"Fit for editing · 10% minimum · Shift+F");commands.Children.Add(fitEdit);
        documentTitle.FontSize=14;documentTitle.FontWeight=FontWeight.Normal;documentTitle.Padding=new(2,9);documentTitle.MinWidth=0;documentTitle.MaxWidth=550;documentTitle.HorizontalAlignment=HorizontalAlignment.Stretch;Tip(documentTitle,"Paper title");toolbar.Children.Add(documentTitle);
        var columns=new Grid{ColumnDefinitions=new("218,*,320")};Grid.SetRow(columns,1);root.Children.Add(columns);
        var libraryPanel=new DockPanel{Margin=new(10,12)};
        var libraryHeader=new DockPanel{Margin=new(4,0,0,10)};DockPanel.SetDock(libraryHeader,Dock.Top);libraryPanel.Children.Add(libraryHeader);
        libraryActionsButton=GlyphButton("···","Graph and category actions",()=>ShowLibraryActions(libraryTree.SelectedItem as LibraryRow??CurrentGraph,libraryActionsButton,PlacementMode.BottomEdgeAlignedLeft),28,20);libraryActionsButton.Height=32;DockPanel.SetDock(libraryActionsButton,Dock.Right);libraryHeader.Children.Add(libraryActionsButton);
        var newGraph=GlyphButton("＋","New graph",()=>NewGraphInCategory((libraryTree.SelectedItem as GraphCategory)?.Id??CurrentGraph.CategoryId),32);newGraph.Height=32;newGraph.Padding=new(0);DockPanel.SetDock(newGraph,Dock.Right);libraryHeader.Children.Add(newGraph);
        var newCategory=GlyphButton("▣＋","New category",()=>_=CreateCategory(),38,16);newCategory.Height=32;newCategory.Padding=new(0);DockPanel.SetDock(newCategory,Dock.Right);libraryHeader.Children.Add(newCategory);
        var libraryLabel=Label("Graphs");libraryLabel.FontSize=13;libraryLabel.FontWeight=FontWeight.Normal;libraryLabel.VerticalAlignment=VerticalAlignment.Center;Paint(()=>libraryLabel.Foreground=Ui("MutedBrush"));libraryHeader.Children.Add(libraryLabel);
        InitializeLibraryUi();libraryPanel.Children.Add(libraryTree);columns.Children.Add(Surface(libraryPanel,new(0,0,1,0)));
        Grid.SetColumn(workspace,1);columns.Children.Add(workspace);graphArea.Children.Add(Graph);workspace.Children.Add(graphArea);BuildMacControls();
        backButton=GlyphButton("‹","Back · Esc",Leave,36,28);backButton.Height=36;backButton.Padding=new(0);backButton.HorizontalAlignment=HorizontalAlignment.Left;backButton.VerticalAlignment=VerticalAlignment.Top;backButton.Margin=new(18);Paint(()=>backButton.Background=Ui("SurfaceBrush"));Grid.SetRow(backButton,1);graphArea.Children.Add(backButton);
        var multiActions=new StackPanel{Orientation=Orientation.Horizontal};
        frameButton=Button("",FrameAroundSelection);frameButton.Content=new ObjectGlyph{Kind="frame"};frameButton.Width=48;frameButton.Height=36;Tip(frameButton,"Create rectangular group");multiActions.Children.Add(frameButton);
        circleButton=Button("",CreateCircle);circleButton.Content=new ObjectGlyph{Kind="circle"};circleButton.Width=48;circleButton.Height=36;Tip(circleButton,"Group connected points");multiActions.Children.Add(circleButton);
        deleteButton=Button("Delete",DeleteSelection);deleteButton.Width=72;deleteButton.Height=36;multiActions.Children.Add(deleteButton);multiBar.Child=multiActions;Grid.SetRow(multiBar,1);graphArea.Children.Add(multiBar);
        Grid.SetRow(fullTextDivider,1);workspace.Children.Add(fullTextDivider);BuildFullTextUi();Grid.SetRow(textArea,2);workspace.Children.Add(textArea);
        var card=Surface(editorLayout,new(1,0,0,0));Grid.SetColumn(card,2);columns.Children.Add(card);
        var head=new DockPanel{LastChildFill=true};editorLayout.Children.Add(head);
        moreButton=GlyphButton("···","More",()=>ShowGraphContext(moreButton),34,20);DockPanel.SetDock(moreButton,Dock.Right);head.Children.Add(moreButton);
        linkButton=GlyphButton("↗","Connect to…",BeginLink,34,22);DockPanel.SetDock(linkButton,Dock.Right);head.Children.Add(linkButton);
        colorButton=Button("",()=>ShowColors(colorButton));colorButton.Content=nodeGlyph;colorButton.Padding=new(5,8);Tip(colorButton,"Mark color");DockPanel.SetDock(colorButton,Dock.Left);head.Children.Add(colorButton);
        DockPanel.SetDock(editorSymbol,Dock.Left);head.Children.Add(editorSymbol);heading.FontSize=12;heading.FontWeight=FontWeight.Normal;heading.Margin=new(7,0,0,0);heading.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(heading);
        Grid.SetRow(emptyEditor,1);editorLayout.Children.Add(emptyEditor);localize.Add(()=>emptyEditor.Text=T("Select an object"));
        var editorBody=new Grid{RowDefinitions=new("Auto,*")};Grid.SetRow(editorBody,1);editorLayout.Children.Add(editorBody);
        captionBox.AcceptsReturn=false;captionBox.Padding=new(4,8);captionPanel.Child=captionBox;editorBody.Children.Add(captionPanel);Tip(captionBox,"Title");
        bodyBox.MinHeight=0;bodyBox.FontSize=18;bodyBox.Padding=new(4,10,9,10);bodyBox.Margin=new(0,8,0,0);bodyBox.VerticalContentAlignment=VerticalAlignment.Top;ScrollViewer.SetVerticalScrollBarVisibility(bodyBox,ScrollBarVisibility.Auto);Grid.SetRow(bodyBox,1);editorBody.Children.Add(bodyBox);Tip(bodyBox,"Proposition");
        edgePanel.Spacing=0;edgePanel.Margin=new(3,12,3,12);var roleLabel=Label("Text role");roleLabel.FontSize=12;roleLabel.FontWeight=FontWeight.Normal;roleLabel.Margin=new(0,0,0,5);edgePanel.Children.Add(roleLabel);edgePanel.Children.Add(roleButtons);
        var orderLabel=Label("Branch order");orderLabel.FontSize=12;orderLabel.FontWeight=FontWeight.Normal;orderLabel.VerticalAlignment=VerticalAlignment.Center;orderPanel.Children.Add(orderLabel);textOrder.Classes.Add("outline");orderPanel.Children.Add(textOrder);edgePanel.Children.Add(orderPanel);edgePanel.Children.Add(edgeLabel);edgePanel.Children.Add(directionButtons);edgePanel.Children.Add(relationButtons);
        var custom=Button("Custom…",()=>_=CustomRelation());custom.HorizontalAlignment=HorizontalAlignment.Left;custom.FontSize=13;custom.Margin=new(0,16,0,0);edgePanel.Children.Add(custom);
        var edgeScroll=new ScrollViewer{Content=edgePanel,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(edgeScroll,1);editorBody.Children.Add(edgeScroll);
        var notesLayout=new DockPanel();notesPanel.Child=notesLayout;Grid.SetRow(notesPanel,2);editorLayout.Children.Add(notesPanel);
        var notesHead=new DockPanel{LastChildFill=true,Margin=new(4,8,4,5)};DockPanel.SetDock(notesHead,Dock.Top);notesLayout.Children.Add(notesHead);
        notePages.ItemsSource=new[]{"1","2","3","4"};notePages.Width=112;DockPanel.SetDock(notePages,Dock.Right);notesHead.Children.Add(notePages);
        var notesLabel=Label("Notes");notesLabel.FontSize=12;notesLabel.FontWeight=FontWeight.Normal;notesLabel.VerticalAlignment=VerticalAlignment.Center;notesHead.Children.Add(notesLabel);
        notesBox.MinHeight=0;notesBox.FontSize=14;notesBox.Padding=new(4,6,9,6);notesBox.VerticalContentAlignment=VerticalAlignment.Top;ScrollViewer.SetVerticalScrollBarVisibility(notesBox,ScrollBarVisibility.Auto);notesLayout.Children.Add(notesBox);Tip(notesBox,"Notes");
        status.FontSize=13;status.MaxWidth=520;status.TextWrapping=TextWrapping.Wrap;var toastContent=new StackPanel{Orientation=Orientation.Horizontal};toastContent.Children.Add(status);toastUndo=Button("Undo",Undo);toastUndo.Padding=new(12,0,0,0);toastUndo.IsVisible=false;toastContent.Children.Add(toastUndo);toast.Child=toastContent;Grid.SetRow(toast,1);graphArea.Children.Add(toast);toastTimer.Tick+=(s,e)=>{toastTimer.Stop();toast.IsVisible=false;};
        Paint(()=>{foreach(var border in new[]{captionPanel,notesPanel,multiBar,toast})border.BorderBrush=Ui("BorderBrush");multiBar.Background=toast.Background=Ui("SurfaceBrush");fullTextDivider.Background=Ui("BorderBrush");heading.Foreground=emptyEditor.Foreground=notesLabel.Foreground=roleLabel.Foreground=orderLabel.Foreground=editorSymbol.Stroke=Ui("MutedBrush");foreach(var b in new[]{frameButton,circleButton})((ObjectGlyph)b.Content!).Stroke=Ui("MutedBrush");});
        textOrder.PropertyChanged+=(s,e)=>{if(e.Property!=TextBox.TextProperty||updating||SelectedObject() is not Relation edge)return;var value=textOrder.Text?.Trim()??"";if(value.Length>0&&(!int.TryParse(value,out var n)||n<0||n>9999))return;var order=value.Length==0?0:int.Parse(value);if(order==edge.TextOrder)return;RememberEdit("text-order:"+edge.Id);edge.TextOrder=order;Changed(false);};
        localize.Add(()=>{captionBox.Watermark=T("Title");UpdateConnectionButton();RefreshLibraryTree();});
        foreach(var box in new[]{documentTitle,captionBox,bodyBox,notesBox})ConfigureTextMenu(box);collectLabels=false;foreach(var refresh in localize)refresh();SetFullTextVisible(fullTextVisible,false);
    }
    void ConfigureTextMenu(TextBox box)
    {
        var undoItem=MenuAction("Undo",box.Undo,"Meta+Z");var redoItem=MenuAction("Redo",box.Redo,"Meta+Shift+Z");
        var menu=new ContextMenu{ItemsSource=new object[]{undoItem,redoItem,new Separator(),MenuAction("Cut",box.Cut,"Meta+X"),MenuAction("Copy",box.Copy,"Meta+C"),MenuAction("Paste",box.Paste,"Meta+V"),MenuAction("Delete",()=>box.SelectedText=""),new Separator(),MenuAction("Select all",box.SelectAll,"Meta+A")}};
        menu.Opening+=(s,e)=>{undoItem.IsEnabled=box.CanUndo;redoItem.IsEnabled=box.CanRedo;};box.ContextMenu=menu;
    }
    void BuildMenus()
    {
        MenuItem Menu(string title){var item=new MenuItem{Header=T(title)};localize.Add(()=>item.Header=T(title));return item;}
        var f=Menu("File");var edit=Menu("Edit");var insert=Menu("Insert");var view=Menu("View");var help=Menu("Help");mainMenu.ItemsSource=new[]{f,edit,insert,view,help};
        f.ItemsSource=new[]{MenuAction("New paper",NewDocument),MenuAction("Open…",()=>_=OpenPicker(),"Meta+O"),MenuAction("Save",()=>Save(),"Meta+S"),MenuAction("Save as…",()=>_=SaveAs()),MenuAction("Show in folder",ShowGraphFolder),MenuAction("Export Markdown…",()=>_=ExportMarkdown())};
        var undoItem=MenuAction("Undo",Undo,"Meta+Z");var redoItem=MenuAction("Redo",Redo,"Meta+Shift+Z");edit.ItemsSource=new object[]{undoItem,redoItem,MenuAction("Copy graph selection",()=>_=CopyGraph(),"Meta+C"),MenuAction("Paste into graph",()=>_=PasteGraph(),"Meta+V"),MenuAction("Selection actions",()=>ShowGraphContext(canvasActionsButton)),MenuAction("Delete selection",DeleteSelection,"Back")};edit.SubmenuOpened+=(s,e)=>{undoItem.IsEnabled=undo.Count>0;redoItem.IsEnabled=redo.Count>0;};
        var frame=MenuAction("Create □",BeginRegion,"R");frame.Icon=new ObjectGlyph{Kind="frame"};var circle=MenuAction("Create ◎",CreateCircle,"Meta+G");circle.Icon=new ObjectGlyph{Kind="circle"};insert.ItemsSource=new[]{MenuAction("Proposition",()=>AddNode(FreePosition()),"Meta+N"),frame,circle};
        var links=Menu("Connections");links.ItemsSource=new[]{(ConnectionDisplay.All,"All"),(ConnectionDisplay.WithinFrames,"Within □"),(ConnectionDisplay.AcrossFrames,"Across □")}.Select(pair=>{var item=MenuAction(pair.Item2,()=>SetConnectionDisplay(pair.Item1));item.ToggleType=MenuItemToggleType.CheckBox;links.SubmenuOpened+=(s,e)=>item.IsChecked=Graph.ConnectionDisplay==pair.Item1;return item;}).ToArray();
        var full=MenuAction("Live full text",ToggleFullText);full.ToggleType=MenuItemToggleType.CheckBox;view.SubmenuOpened+=(s,e)=>full.IsChecked=fullTextVisible;
        var titles=MenuAction("Show titles",()=>Graph.ShowCaptions=!Graph.ShowCaptions,"Shift+Space");titles.ToggleType=MenuItemToggleType.CheckBox;view.SubmenuOpened+=(s,e)=>titles.IsChecked=Graph.ShowCaptions;
        var language=Menu("Language");language.ItemsSource=Localization.Languages.Select(pair=>{var item=MenuAction(pair.Name,()=>SetLanguage(pair.Code));item.ToggleType=MenuItemToggleType.Radio;language.SubmenuOpened+=(s,e)=>item.IsChecked=Localization.Language==pair.Code;return item;}).ToArray();
        view.ItemsSource=new object[]{MenuAction("Select",()=>SetCanvasTool(false),"V"),MenuAction("Pan",()=>SetCanvasTool(true),"H"),titles,MenuAction("Fit all",()=>Graph.Fit(),"F"),MenuAction("Fit for editing",()=>Graph.Fit(editing:true),"Shift+F"),MenuAction("Focus selection / return",()=>Graph.ToggleDetail(),"Z"),MenuAction("Arrange inside",()=>Graph.ArrangeNaturally()),MenuAction("Switch theme",()=>{dark=!dark;ApplyTheme();SaveSettings();}),links,full,language};
        help.ItemsSource=new[]{MenuAction("Controls and symbols",()=>_=ShowHelp())};
    }
    void SetLanguage(string language){Localization.Language=Localization.Normalize(language);foreach(var refresh in localize)refresh();ApplyTheme();RefreshFullText(true);SaveSettings();}
    void SetConnectionDisplay(ConnectionDisplay value){if(Graph.IsInteracting)return;Graph.ConnectionDisplay=value;UpdateConnectionButton();SaveSettings();Graph.Focus();}
    void UpdateConnectionButton(){if(connectionButton==null)return;var label=Graph.ConnectionDisplay switch{ConnectionDisplay.WithinFrames=>"Within",ConnectionDisplay.AcrossFrames=>"Across",_=>"All"};connectionButton.Content=T("Links: ")+T(label);Tip(connectionButton,"Show all, within-frame or cross-frame connections");}
    void BuildArrowChoices(Relation edge)
    {
        roleButtons.Children.Clear();foreach(var (value,label) in new[]{("flow","Body"),("reference","Reference")}){var b=Button(label,()=>SetTextRole(edge.Id,value));b.Margin=new(2);b.Classes.Set("selected",(edge.TextRole??"flow")==value);roleButtons.Children.Add(b);}
        orderPanel.IsVisible=!GraphFullText.IsReference(edge);textOrder.Text=edge.TextOrder==0?"":edge.TextOrder.ToString();edgeLabel.Text=T(GraphStyle.RelationName(edge.Label));
        directionButtons.Children.Clear();foreach(var (value,symbol,tip) in new[]{("reverse","←","Reverse arrow"),("forward","→","Forward arrow"),("both","↔","Both directions")}){var b=Button(symbol,()=>SetDirection(edge.Id,value));b.FontSize=24;b.Height=42;b.Margin=new(2);b.Classes.Set("selected",edge.Direction==value);Tip(b,tip);directionButtons.Children.Add(b);}
        relationButtons.Children.Clear();foreach(var value in GraphStyle.Relations){var b=Button("",()=>SetRelation(edge.Id,value));b.Content=new EdgeGlyph{Label=value,Stroke=GraphStyle.Brush(dark?"#E1E6ED":"#273747"),Surface=GraphStyle.Brush(dark?"#232730":"#FFFFFF")};b.Height=55;b.Margin=new(2);b.Classes.Set("selected",GraphStyle.RelationName(edge.Label)==value);Tip(b,value);relationButtons.Children.Add(b);}
    }
}
public sealed class ObjectGlyph:Control
{
    public string Kind{get;set;}="frame";
    public IBrush Stroke{get;set;}=new SolidColorBrush(Color.Parse("#7C8997"));
    public ObjectGlyph(){Width=22;Height=22;IsHitTestVisible=false;}
    public override void Render(DrawingContext dc){var size=Math.Min(Bounds.Width,Bounds.Height);var center=new Point(Bounds.Width/2,Bounds.Height/2);var radius=size*.38;var pen=new Pen(Stroke,1.7);if(Kind=="circle"){dc.DrawEllipse(null,pen,center,radius,radius);dc.DrawEllipse(null,pen,center,radius*.58,radius*.58);}else if(Kind=="point")dc.DrawEllipse(Stroke,null,center,radius,radius);else dc.DrawRectangle(null,pen,new Rect(center.X-radius,center.Y-radius,radius*2,radius*2),2,2);}
}
