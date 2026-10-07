using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
namespace Papergraph;
public partial class MainWindow
{
    void RunIconParityTests()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        Check(Icon!=null&&(!OperatingSystem.IsMacOS()||MacApplicationIcon.NativeIconApplied),"Original app icon is loaded and assigned to the running native Mac application");
        var originalTheme=dark;
        dark=false;ApplyTheme();UpdateLayout();
        foreach(var name in new[]{"moon","sun"})
        {
            var glyph=(ToolbarGlyph)themeButton.Content!;
            Check(glyph.Symbol==(dark?"☀":"☾")&&Equals(ToolTip.GetTip(themeButton),T(dark?"Switch to light theme":"Switch to dark theme")),"Theme click changes both the symbol and the destination tooltip");
            using(var bitmap=new RenderTargetBitmap(new PixelSize(168,168),new Vector(768,768)))
            {bitmap.Render(glyph);bitmap.Save(Path.Combine(dataDir,$"theme-{name}-8x.png"));}
            using(var bitmap=new RenderTargetBitmap(new PixelSize(78,84),new Vector(192,192)))
            {bitmap.Render(themeButton);bitmap.Save(Path.Combine(dataDir,$"theme-{name}-button-2x.png"));}
            themeButton.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));UpdateLayout();
        }
        Check(!dark,"Two theme-button clicks return to the initial theme");
        dark=originalTheme;ApplyTheme();SaveSettings();
    }
}
