using Rhino.Display;
using Rhino.PlugIns;
namespace Aholo.Rhino;

/// <summary>Native mode copies are short-lived; only the exact owned mode is updated, never deleted/replaced.</summary>
internal sealed class RhinoGaussianModeStore : IGaussianModeStore
{
    private readonly PlugIn _plugin;
    private readonly string _setting;
    internal RhinoGaussianModeStore(PlugIn plugin,string setting){_plugin=plugin;_setting=setting;}
    public GaussianDisplayModeSnapshot? Get(Guid id)
    {
        using var mode=DisplayModeDescription.GetDisplayMode(id);
        return mode is null?null:Snapshot(mode);
    }
    public IEnumerable<GaussianDisplayModeSnapshot> FindAll()
    {
        var modes=DisplayModeDescription.GetDisplayModes();
        try{return modes.Select(Snapshot).ToArray();}
        finally{foreach(var mode in modes)mode.Dispose();}
    }
    public Guid CopyShaded(string englishName)
    {
        Guid id=DisplayModeDescription.CopyDisplayMode(DisplayModeDescription.ShadedId,englishName);
        GaussianLog.Write("mode_created",$"id={id:D}");return id;
    }
    public bool Update(GaussianDisplayModeSnapshot desired)
    {
        using var mode=DisplayModeDescription.GetDisplayMode(desired.Id);
        if(mode is null||mode.EnglishName!=desired.EnglishName)return false;
        mode.InMenu=desired.InMenu;mode.AllowObjectAssignment=desired.AllowObjectAssignment;
        mode.SupportsShading=desired.SupportsShading;mode.ShadedPipelineRequired=desired.ShadedPipelineRequired;
        mode.WireframePipelineRequired=desired.WireframePipelineRequired;
        mode.DisplayAttributes.ShadingEnabled=desired.ShadingEnabled;
        mode.DisplayAttributes.DisableTransparency=desired.DisableTransparency;
        mode.DisplayAttributes.ShadeVertexColors=desired.ShadeVertexColors;
        mode.DisplayAttributes.FrontFlatShaded=desired.FrontFlatShaded;
        bool updated=DisplayModeDescription.UpdateDisplayMode(mode);
        GaussianLog.Write("mode_update",$"id={desired.Id:D}; native_result={updated}; readback_required=true");
        return updated;
    }
    public void SaveId(Guid id)=>_plugin.Settings.SetString(_setting,id.ToString("D"));
    private static GaussianDisplayModeSnapshot Snapshot(DisplayModeDescription mode)=>new(mode.Id,mode.EnglishName,
        mode.InMenu,mode.AllowObjectAssignment,mode.SupportsShading,mode.ShadedPipelineRequired,mode.WireframePipelineRequired,
        mode.DisplayAttributes.ShadingEnabled,mode.DisplayAttributes.DisableTransparency,mode.DisplayAttributes.ShadeVertexColors,mode.DisplayAttributes.FrontFlatShaded);
}
