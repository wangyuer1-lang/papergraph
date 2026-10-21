using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
namespace Papergraph;
internal static class Program
{
    internal static string DataDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Papergraph");
    internal static string? OpenFile;
    internal static bool SelfTest;
    internal static bool BluetoothUiTest;
    internal static bool AgentProtectionTest;
    internal static bool EditorInteractionTest;
    internal static bool TextColorTest;
    internal static bool FullTextEditTest;
    [STAThread] public static int Main(string[] args)
    {
        if(args.Contains("--lan-sync-test")){try{var ix=Array.IndexOf(args,"--data-dir");var root=ix>=0&&ix+1<args.Length?args[ix+1]:Path.Combine(Path.GetTempPath(),"papergraph-sync-test-"+Guid.NewGuid().ToString("N"));LanSyncTests.Run(root).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 2;}}
        BluetoothUiTest=args.Contains("--bluetooth-ui-test");
        FullTextEditTest=args.Contains("--full-text-edit-test");
        TextColorTest=args.Contains("--text-color-test");
        AgentProtectionTest=args.Contains("--agent-protection-test");
        EditorInteractionTest=args.Contains("--editor-interaction-test");
        SelfTest=args.Contains("--self-test")||AgentProtectionTest||EditorInteractionTest||TextColorTest||BluetoothUiTest||FullTextEditTest;
        var index=Array.IndexOf(args,"--data-dir");if(index>=0&&index+1<args.Length)DataDirectory=Path.GetFullPath(args[index+1]);
        if(FullTextEditTest&&(index<0||(Directory.Exists(DataDirectory)&&Directory.EnumerateFiles(DataDirectory,"*.papergraph").Any()))){Console.Error.WriteLine("Full text tests require a fresh --data-dir.");return 2;}
        if(SelfTest)TestEvidence.OutputDirectory=DataDirectory;
        var request=Array.IndexOf(args,"--agent-request");var response=Array.IndexOf(args,"--agent-response");
        if(request>=0){if(request+1>=args.Length||response<0||response+1>=args.Length)return 2;return AgentBridge.Client(DataDirectory,args[request+1],args[response+1]).GetAwaiter().GetResult();}
        OpenFile=args.FirstOrDefault(a=>a.EndsWith(".papergraph",StringComparison.OrdinalIgnoreCase)||a.EndsWith(".yujian",StringComparison.OrdinalIgnoreCase));
        BluetoothSync.StartHost = MacBluetooth.Start; BluetoothSync.QrImage = MacBluetooth.Qr;
        return AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new AvaloniaNativePlatformOptions { OverlayPopups=true })
            .WithInterFont().LogToTrace().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App:Application
{
    AgentBridge? agent;FileStream? instance;
    public override void Initialize(){Name="Papergraph";Styles.Add(new FluentTheme());Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://papergraph/")){Source=new Uri("avares://papergraph/PapergraphStyles.axaml")});RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Directory.CreateDirectory(Program.DataDirectory);
            try{instance=new FileStream(Path.Combine(Program.DataDirectory,".instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){Console.Error.WriteLine("This library is already open.");desktop.Shutdown(1);return;}
            var window=new MainWindow(Program.DataDirectory);desktop.MainWindow=window;
            agent=new AgentBridge(Program.DataDirectory,new System.Windows.Threading.Dispatcher(),window.HandleAgent);
            desktop.Exit+=(s,e)=>{agent.Dispose();instance.Dispose();};
            if(Program.OpenFile!=null)window.OpenPath(Program.OpenFile);
            if(Program.SelfTest)window.Opened+=(s,e)=>Avalonia.Threading.Dispatcher.UIThread.Post(async()=>{
                try{if(Program.FullTextEditTest){await window.RunFullTextEditTests();Console.WriteLine("PASS: full text editing tests");}else if(Program.BluetoothUiTest){await window.RunBluetoothUiTests();Console.WriteLine("PASS: native Bluetooth UI tests");}else if(Program.TextColorTest){await window.RunTextColorTests();Console.WriteLine("PASS: text color tests");}else if(Program.EditorInteractionTest){await window.RunEditorInteractionTests();Console.WriteLine("PASS: editor interaction tests");}else if(Program.AgentProtectionTest){await window.RunAgentProtectionTests();Console.WriteLine("PASS: agent protection tests");}else{await window.RunSelfTests();Console.WriteLine("PASS: all macOS tests");}desktop.Shutdown(0);}
                catch(Exception ex){Console.Error.WriteLine(ex);File.WriteAllText(Path.Combine(Program.DataDirectory,"test-failure.txt"),ex.ToString());desktop.Shutdown(1);}
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}
