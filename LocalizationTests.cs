using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Papergraph;

public static class LocalizationTests
{
    public static void Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        var dir=Path.Combine(Path.GetTempPath(),"papergraph-localization-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var originalCulture=CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture=CultureInfo.GetCultureInfo("en-US");
        File.WriteAllText(Path.Combine(dir,"settings.json"),"{\"language\":\"en\",\"customSetting\":42}");
        var window=new MainWindow(dir);
        try
        {
            window.Show();window.UpdateLayout();window.RefreshFullText();
            var doc=window.Graph.Document;doc.Nodes[0].Title="Support 日本語 中文";doc.Nodes[0].Note="File Save Notes";
            var before=doc.Serialize();
            foreach(var code in new[]{"zh-CN","ja","en"})
            {
                window.SetLanguage(code);window.UpdateLayout();
                Check((string)window.FileMenu.Header==Localization.Text("_File",code),"Top menus update without restart");
                Check((string)window.FitButton.Content==Localization.Text("Fit all",code),"Toolbar updates without restart");
                Check((string)window.ConnectionButton.Content==Localization.Text("Links: ",code)+Localization.Text("All",code),"Connection state updates in the selected language");
                Check((string)window.PropositionBox.ContextMenu.Items.OfType<MenuItem>().First().Header==Localization.Text("Undo",code),"Editor popup updates without restart");
                Check(doc.Serialize()==before,"Switching language must not change graph content or canonical relations");
                var settings=JsonNode.Parse(File.ReadAllText(Path.Combine(dir,"settings.json")))!;
                Check(settings["language"]!.GetValue<string>()==code&&settings["customSetting"]!.GetValue<int>()==42,"Preference persists without losing unknown settings");
                foreach(var key in Localization.Keys)
                {
                    var translated=Localization.Text(key,code);Check(translated.Length>0,"Translation cannot be empty");
                    // Format every translated string with ample arguments to catch invalid placeholders.
                    if(key.Contains("{0}"))_ = string.Format(translated,0,1,2,3);
                }
                var image=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(window);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(Path.Combine(AppContext.BaseDirectory,"language-"+code+".png"));png.Save(output);
            }
            window.SetLanguage("ja");window.Close();
            var reopened=new MainWindow(dir);Check((string)reopened.FileMenu.Header==Localization.Text("_File","ja"),"Saved language is restored on startup");reopened.Close();
            File.WriteAllText(Path.Combine(dir,"settings.json"),"{\"language\":\"invalid\"}");
            var fallback=new MainWindow(dir);Check((string)fallback.FileMenu.Header=="_File","Unsupported preferences fall back to English");fallback.Close();
            Check(Localization.Text("a user-defined relation","ja")=="a user-defined relation","Unknown text is preserved");
            Check(Localization.Normalize("ja-JP")=="ja"&&Localization.Normalize("zh-CN")=="zh-CN","OS cultures normalize to supported locales");
        }
        finally {window.Close();CultureInfo.CurrentUICulture=originalCulture;Localization.Apply(new ResourceDictionary(),"en");}
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),"PASS: live language switching, editor menus, persistence, preference compatibility, unchanged graph data, locale formatting and startup fallback.\n");
    }
}
