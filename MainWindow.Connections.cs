using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Papergraph;

public partial class MainWindow
{
    void InitializeConnectionControls()
    {
        ConnectionButton.Click+=(s,e)=>SetConnectionDisplay(Graph.ConnectionDisplay switch
        {
            ConnectionDisplay.All=>ConnectionDisplay.WithinFrames,
            ConnectionDisplay.WithinFrames=>ConnectionDisplay.AcrossFrames,
            _=>ConnectionDisplay.All
        });
        UpdateConnectionButton();
    }
    void UpdateConnectionButton()
    {
        var label=Graph.ConnectionDisplay switch
        {
            ConnectionDisplay.WithinFrames=>"Within",
            ConnectionDisplay.AcrossFrames=>"Across",
            _=>"All"
        };
        ConnectionButton.Content=Localization.Text("Links: ")+Localization.Text(label);
        ConnectionButton.ToolTip=Localization.Text("Click to cycle: All → Within □ → Across □\nWithin: endpoints share an innermost □. Across: different innermost □, or a □ to outside.\nOuter wrappers do not join separate inner □. Unframed links appear in All. Only visibility changes.");
        AutomationProperties.SetName(ConnectionButton,Localization.Text("Show connections: ")+Localization.Text(label));
    }
    void SetConnectionDisplay(ConnectionDisplay display)
    {
        if(Graph.IsInteracting)return;
        Graph.ConnectionDisplay=display;UpdateConnectionButton();SaveViewSettings();Graph.Focus();
    }
    void BuildConnectionMenu()
    {
        var menu=new MenuItem{Header=Localization.Text("Connections")};
        foreach(var (display,label) in new[]{(ConnectionDisplay.All,"All"),(ConnectionDisplay.WithinFrames,"Within □"),(ConnectionDisplay.AcrossFrames,"Across □")})
        {
            var item=Item(menu,label,()=>SetConnectionDisplay(display));item.IsCheckable=true;
            menu.SubmenuOpened+=(s,e)=>item.IsChecked=Graph.ConnectionDisplay==display;
        }
        Localization.Bind(menu,HeaderedItemsControl.HeaderProperty,"Connections");ViewMenu.Items.Add(menu);
    }
    void LoadViewSettings()
    {
        try
        {
            var path=Storage.CompatiblePath(dataDir,"settings.json","\u754c\u9762.json");
            language=Localization.SystemLanguage;
            if(!File.Exists(path))return;
            var settings=JsonNode.Parse(File.ReadAllText(path));
            if(settings?["collapsedCategories"] is JsonArray collapsed)foreach(var item in collapsed)if(item?.GetValue<string>() is string id)collapsedCategories.Add(id);
            language=Localization.Normalize(settings?["language"]?.GetValue<string>()??Localization.SystemLanguage);
            dark=settings?["dark"]?.GetValue<bool>()??false;
            fullTextVisible=settings?["fullTextVisible"]?.GetValue<bool>()??false;
            if(Enum.TryParse<ConnectionDisplay>(settings?["connectionDisplay"]?.GetValue<string>(),out var mode)&&Enum.IsDefined(mode))Graph.ConnectionDisplay=mode;
        }
        catch { }
    }
    void SaveViewSettings()
    {
        try
        {
            var path=Path.Combine(dataDir,"settings.json");JsonObject settings;
            try{settings=JsonNode.Parse(File.ReadAllText(path)) as JsonObject??new();}catch{settings=new();}
            settings["collapsedCategories"]=new JsonArray(collapsedCategories.Select(id=>JsonValue.Create(id)).ToArray());
            settings["dark"]=dark;settings["connectionDisplay"]=Graph.ConnectionDisplay.ToString();
            settings["fullTextVisible"]=fullTextVisible;settings["language"]=language;
            File.WriteAllText(path,settings.ToJsonString());
        }
        catch { }
    }
}
