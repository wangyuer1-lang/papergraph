using System.IO;
using System.Windows;
using System.Threading;
namespace Papergraph;
public partial class App : Application
{
    Mutex? instance;
    AgentBridge? agent;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if(e.Args.Contains("--self-test")) { ShutdownMode=ShutdownMode.OnExplicitShutdown;File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-failure.txt"),"");try { System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US"); ModelTests.Run(); FullTextTests.Run(); GripZoomTests.Run(); GraphTests.Run(); BoardTests.Run(); ArrangeTests.Run(); SelectionTests.Run(); ClipboardTests.Run(); SpacingTests.Run(); ViewportTests.Run(); FitTests.Run(); ConnectionTests.Run(); MarkColorTests.Run(); EditorTests.Run(); CaptionTests.Run(); CaptionLayoutTests.Run(); NavigationTests.Run(); AgentTests.Run(); AgentLibraryTests.Run(); OpenGroupTests.Run(); DragCommitTests.Run(); RingConnectionTests.Run(); TextMenuTests.Run(); LibraryTests.Run(); CategoryTests.Run(); FrameFeatureTests.Run(); NotePageTests.Run(); FrameNavigationTests.Run(); LocalizationTests.Run(); var fixture=Array.IndexOf(e.Args,"--text-fixture"); if(fixture>=0&&fixture+2<e.Args.Length)FullTextTests.VerifyFixture(e.Args[fixture+1],e.Args[fixture+2]); Shutdown(0); } catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-failure.txt"),ex.ToString()); Shutdown(1); } return; }
        var ix=Array.IndexOf(e.Args,"--data-dir");
        var dataDir=ix>=0&&ix+1<e.Args.Length?Path.GetFullPath(e.Args[ix+1]):Path.Combine(AppContext.BaseDirectory,"Data");
        var agentIndex=Array.IndexOf(e.Args,"--agent-request");
        if(agentIndex>=0)
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;var outputIndex=Array.IndexOf(e.Args,"--agent-response");
            if(agentIndex+1>=e.Args.Length||outputIndex<0||outputIndex+1>=e.Args.Length){Shutdown(2);return;}
            var exit=await AgentBridge.Client(dataDir,e.Args[agentIndex+1],e.Args[outputIndex+1]);Shutdown(exit);return;
        }
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDir.ToLowerInvariant())))[..20];
        instance=new Mutex(true,"Local\\Yujian_"+hash,out var first);
        if(!first) { MessageBox.Show("This library is already open. Switch to the papergraph window.","papergraph"); Shutdown(); return; }
        DispatcherUnhandledException+=(s,a)=> { try { File.WriteAllText(Path.Combine(dataDir,"error.log"),a.Exception.ToString()); } catch {} MessageBox.Show("The operation could not finish. Your content is still in this window.\n"+a.Exception.Message,"papergraph"); a.Handled=true; };
        try { var window=new MainWindow(dataDir);window.Show();agent=new AgentBridge(dataDir,Dispatcher,window.HandleAgent); } catch(Exception ex) { MessageBox.Show("Could not open the library:\n"+ex.Message,"papergraph"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { agent?.Dispose();instance?.Dispose(); base.OnExit(e); }
}



