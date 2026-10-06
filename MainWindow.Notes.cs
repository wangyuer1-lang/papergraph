using System.Windows;
using System.Windows.Controls;
namespace Papergraph;

public partial class MainWindow
{
    readonly Dictionary<string,int> noteSelections=[];
    int notePageIndex;
    bool refreshingNotes;
    RadioButton[] NotePageButtons => [NotePage1,NotePage2,NotePage3,NotePage4];
    IGraphNotes? NoteOwner => editorKind switch
    {
        "node"=>doc.Node(editorId),
        "region"=>doc.Frame(editorId),
        "edge"=>doc.Edges.FirstOrDefault(e=>e.Id==editorId),
        _=>null
    };
    string NoteSelectionKey => file+"|"+editorKind+"|"+editorId;
    void InitializeNotes()
    {
        NotesBox.TextChanged+=(s,e)=>
        {
            if(updating||refreshingNotes||NoteOwner is not IGraphNotes owner)return;
            RememberEdit("note:"+editorKind+":"+editorId+":"+notePageIndex);
            if(notePageIndex==0)owner.Note=NotesBox.Text;
            else owner.AdditionalNotes[notePageIndex-1].Body=NotesBox.Text;
            Changed(false);
        };
        foreach(var button in NotePageButtons)button.Checked+=(s,e)=>
        {
            if(updating||refreshingNotes||NoteOwner==null)return;
            noteSelections[NoteSelectionKey]=int.Parse((string)((RadioButton)s).Tag);
            editKey="";RefreshNotes();
        };
    }
    void RefreshNotes()
    {
        refreshingNotes=true;
        try
        {
            if(NoteOwner is not IGraphNotes owner)return;
            notePageIndex=Math.Clamp(noteSelections.GetValueOrDefault(NoteSelectionKey),0,owner.AdditionalNotes.Count);
            for(int i=0;i<4;i++)NotePageButtons[i].IsChecked=i==notePageIndex;
            var text=notePageIndex==0?owner.Note:owner.AdditionalNotes[notePageIndex-1].Body;
            if(NotesBox.Text!=text)SetText(NotesBox,text);
            NotesBox.ToolTip=null;
        }
        finally {refreshingNotes=false;}
    }
}
