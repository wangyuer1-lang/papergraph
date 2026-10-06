using System.Windows.Controls;
using System.Windows;

namespace Papergraph;

public partial class MainWindow
{
    string language="en";
    void InitializeLanguage()
    {
        Localization.Apply(Resources,language);
        var menu=new MenuItem();Localization.Bind(menu,HeaderedItemsControl.HeaderProperty,"Language");
        foreach(var (code,name) in Localization.Languages)
        {
            var item=Item(menu,name,()=>SetLanguage(code));item.IsCheckable=true;
            menu.SubmenuOpened+=(s,e)=>item.IsChecked=language==code;
        }
        ViewMenu.Items.Add(menu);
        Localization.Bind(fullTextButton!,ContentControl.ContentProperty,"Full text");
        Localization.Bind(fullTextButton!,ToolTipProperty,"Show or hide live full text");
        foreach(var editor in new[]{DocumentTitleBox,CaptionBox,PropositionBox,NotesBox})ConfigureTextMenu(editor);
    }
    internal void SetLanguage(string code)
    {
        language=Localization.Normalize(code);Localization.Apply(Resources,language);
        ApplyTheme();UpdateConnectionButton();RefreshFullText();RefreshLibraryTree();SaveViewSettings();
    }
    MenuItem LocalizedItem(ItemsControl menu,string source,Action action,string? shortcut=null,bool enabled=true)
    {
        var item=Item(menu,Localization.Text(source),action,shortcut,enabled);
        Localization.Bind(item,HeaderedItemsControl.HeaderProperty,source);return item;
    }
}
