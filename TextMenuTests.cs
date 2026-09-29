using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Papergraph;

public static class TextMenuTests
{
    public static void Run()
    {
        void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        var directory=Path.Combine(Path.GetTempPath(),"papergraph-text-menu-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"settings.json"),"{\"dark\":true}");
        var window=new MainWindow(directory);
        foreach(var dark in new[]{true,false,true})
        {
            if(window.Graph.Dark!=dark)window.ThemeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach(var editor in new[]{window.PropositionBox,window.NotesBox,window.CaptionBox,window.DocumentTitleBox})
            {
                var menu=editor.ContextMenu;Check(menu!=null,"Each text editor has an explicit themed context menu");
                menu!.ApplyTemplate();menu.Measure(new Size(400,600));menu.Arrange(new Rect(menu.DesiredSize));menu.UpdateLayout();
                var surface=(Border)menu.Template.FindName("MenuSurface",menu);
                var bg=((SolidColorBrush)surface.Background).Color;var fg=((SolidColorBrush)menu.Foreground).Color;
                Check(bg==((SolidColorBrush)window.Resources["SurfaceBrush"]).Color&&fg==((SolidColorBrush)window.Resources["TextBrush"]).Color,"The actual menu surface and text follow theme changes");
                Check(dark?fg.R-bg.R>150:bg.R-fg.R>150,"Text stays legible on the popup background in either theme");
                var items=menu.Items.OfType<MenuItem>().ToArray();Check(items.Length==7&&items.All(i=>i.CommandTarget==editor),"All seven standard editing commands target the original text editor");
                Check(items.Any(i=>i.Command==ApplicationCommands.Copy)&&items.Any(i=>i.Command==ApplicationCommands.Paste),"Copy and paste retain routed editor command behavior");
                foreach(var item in items){item.ApplyTemplate();Check(((SolidColorBrush)item.Foreground).Color==fg,"Menu entries inherit the current readable foreground");}
            }
        }
        window.Close();File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: proposition, notes and title context menus use themed surfaces/text through dark/light/dark switches and preserve targeted editing commands.\n");
    }
}
