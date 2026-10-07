using Avalonia.Input.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Text.Json;
namespace Papergraph;
public partial class MainWindow
{
    static FilePickerFileType GraphFileType=new("Papergraph"){Patterns=new[]{"*.papergraph","*.yujian","*.bak"}};
    async Task OpenPicker(){try{var paths=await StorageProvider.OpenFilePickerAsync(new(){Title=T("Open graph"),AllowMultiple=false,FileTypeFilter=new[]{GraphFileType,FilePickerFileTypes.All}});if(paths.FirstOrDefault()?.TryGetLocalPath() is string path)OpenPath(path);}catch(Exception ex){Notify(ex.Message);}}
    async Task SaveAs()
    {
        try{var selected=await StorageProvider.SaveFilePickerAsync(new(){Title=T("Save as…"),SuggestedFileName=DisplayTitle(doc.Title)+".papergraph",DefaultExtension="papergraph",FileTypeChoices=new[]{GraphFileType}});if(selected?.TryGetLocalPath() is not string path)return;if(!Save())return;Storage.Save(path,doc);file=Path.GetFullPath(path);dirty=false;WriteRecent();RefreshAll();Notify("Saved to "+file);}catch(Exception ex){Notify(ex.Message);}
    }
    async Task ExportMarkdown(){try{var selected=await StorageProvider.SaveFilePickerAsync(new(){Title="Export Markdown",SuggestedFileName=DisplayTitle(doc.Title)+".md",DefaultExtension="md"});if(selected?.TryGetLocalPath() is string path){await File.WriteAllTextAsync(path,doc.Markdown());Notify("Exported to "+path);}}catch(Exception ex){Notify(ex.Message);}}
    void ShowGraphFolder()
    {
        if(!Save())return;var start=new ProcessStartInfo(OperatingSystem.IsMacOS()?"/usr/bin/open":OperatingSystem.IsWindows()?"explorer.exe":"xdg-open"){UseShellExecute=false};
        if(OperatingSystem.IsMacOS()){start.ArgumentList.Add("-R");start.ArgumentList.Add(file);}else start.ArgumentList.Add(Path.GetDirectoryName(file)!);Process.Start(start);
    }
    async Task<string?> Ask(string title,string value)
    {
        var dialog=new Window{Title=T(title),Width=420,Height=170,CanResize=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(18),Spacing=12};var input=new TextBox{Text=value};input.Classes.Add("outline");ConfigureTextMenu(input);panel.Children.Add(input);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Spacing=8};
        var cancel=new Button{Content=T("Cancel")};cancel.Click+=(s,e)=>dialog.Close(null);var ok=new Button{Content="OK"};ok.Click+=(s,e)=>dialog.Close(input.Text);row.Children.Add(cancel);row.Children.Add(ok);panel.Children.Add(row);dialog.Content=panel;dialog.Opened+=(s,e)=>{input.Focus();input.SelectAll();};return await dialog.ShowDialog<string?>(this);
    }
    async Task RenameGraph(){var title=await Ask("Rename graph",doc.Title);if(title==null)return;Remember();doc.Title=title;RefreshAll();Changed(false);}
    async Task CreateCategory(){var title=await Ask("New category","");if(string.IsNullOrWhiteSpace(title))return;Safe(()=>LibraryUi("createCategory",new{title}));}
    void LibraryUi(string operation,object fields){var input=JsonSerializer.SerializeToElement(fields);var values=input.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone());values["operation"]=operation;values["requestId"]=Guid.NewGuid().ToString();values["expectedLibraryRevision"]=catalog.Revision();HandleAgent(JsonSerializer.SerializeToElement(values));}
    async Task MoveCategory()
    {
        var title=await Ask("Move to category (blank for Unfiled)",catalog.Categories.FirstOrDefault(c=>c.Id==catalog.Assignments.GetValueOrDefault(file))?.Name??"");if(title==null)return;
        var target=catalog.Categories.FirstOrDefault(c=>c.Name==title);if(title.Length>0&&target==null){Notify("Create that category first");return;}Safe(()=>LibraryUi("moveGraphs",new{documentPaths=new[]{file},categoryId=target?.Id}));
    }
    Task DeleteGraph()
    {
        if(!Save())return Task.CompletedTask;var target=file;var entry=CurrentGraph;
        var next=library.FirstOrDefault(e=>!SamePath(e.Path,target)&&File.Exists(e.Path));
        if(next!=null)SwitchGraph(next.Path);else NewDocument();
        if(SamePath(file,target)||dirty)return Task.CompletedTask;
        try{if(File.Exists(target))MacTrash.Move(target);library.Remove(entry);TrackCurrentGraph();Notify("Graph moved to Trash");}
        catch(Exception ex){Notify("Could not delete this graph: "+ex.Message);}return Task.CompletedTask;
    }
}
