using System.IO;
namespace Papergraph;

public partial class MainWindow
{
    // Only a human-facing graph menu calls this. It is deliberately not an agent operation.
    internal void SetGraphAgentReadOnly(string path,bool readOnly)
    {
        path=AgentLibraryPath(path);
        bool active=SamePath(path,file);
        if(active&&(Graph.IsInteracting||Graph.DrawingRegion||Graph.LinkMode))
            throw new InvalidOperationException("Finish the active graph gesture before changing AI protection.");
        var document=active?doc:GraphDocument.Parse(File.ReadAllText(path));
        if(document.AgentReadOnly==readOnly)return;
        var previous=document.AgentReadOnly;
        document.AgentReadOnly=readOnly;
        try{Storage.Save(path,document);}
        catch{document.AgentReadOnly=previous;throw;}
        if(active)
        {
            revision++;dirty=false;saveTimer.Stop();
            SaveDot.Fill=GraphStyle.Brush(dark?"#A6BCAF":"#789989");
            SaveDot.ToolTip=Localization.Text("Saved to ")+file;
        }
        var entry=library.FirstOrDefault(e=>SamePath(e.Path,path));
        if(entry!=null)entry.AgentReadOnly=readOnly;
        UpdateLibraryTitle();RefreshLibraryTree();
        Notify(readOnly?"AI read-only enabled · You can still edit this graph":"AI read-only disabled");
    }
}
