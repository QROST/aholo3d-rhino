using System.Runtime.InteropServices;
using Rhino.Commands;
namespace Aholo3D.Rhino;

// Legacy spellings remain loadable commands; all execution goes through the canonical implementation.

[Guid("4ae07b42-2e22-58df-9ce4-a9135c8f7fcb")]
public sealed class LegacyAholoCancelCommand : Command
{
    public override string EnglishName => "AholoCancel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DCancelCommand.Execute(doc,mode);
}

[Guid("2537ea63-2010-5baf-8bf2-bf5d5e4c5e57")]
public sealed class LegacyAholoCancelOperationCommand : Command
{
    public override string EnglishName => "AholoCancelOperation";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DCancelOperationCommand.Execute(doc,mode);
}

[Guid("8c1823d7-af42-54c4-8766-7be85ff355fa")]
public sealed class LegacyAholoExportGsCommand : Command
{
    public override string EnglishName => "AholoExportGs";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DExportGsCommand.Execute(doc,mode);
}

[Guid("ab931e58-c146-50d6-bff5-eb5d2cf6714d")]
public sealed class LegacyAholoExportMeshCommand : Command
{
    public override string EnglishName => "AholoExportMesh";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DExportMeshCommand.Execute(doc,mode);
}

[Guid("d2eb6bb8-3279-530b-ac80-935ee96a0913")]
public sealed class LegacyAholoExportSceneCommand : Command
{
    public override string EnglishName => "AholoExportScene";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DExportSceneCommand.Execute(doc,mode);
}

[Guid("a3b8ba55-0d3f-5765-8e27-a866732b0b0b")]
public sealed class LegacyAholoGsDiagnosticsCommand : Command
{
    public override string EnglishName => "AholoGsDiagnostics";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DGsDiagnosticsCommand.Execute(doc,mode);
}

[Guid("46519cbd-a519-5915-a2df-d87f9606e92c")]
public sealed class LegacyAholoGsModeCommand : Command
{
    public override string EnglishName => "AholoGsMode";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DGsModeCommand.Execute(doc,mode);
}

[Guid("22808eac-71ec-5a81-8216-5975f67a7dce")]
public sealed class LegacyAholoGsProbeCommand : Command
{
    public override string EnglishName => "AholoGsProbe";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DGsProbeCommand.Execute(doc,mode);
}

[Guid("fb24041c-5a3c-5df3-93c9-cca293d3d4a7")]
public sealed class LegacyAholoImportGsCommand : Command
{
    public override string EnglishName => "AholoImportGs";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DImportGsCommand.Execute(doc,mode);
}

[Guid("119afb58-46c1-5215-ae09-c1fc8668b9ed")]
public sealed class LegacyAholoImportMeshCommand : Command
{
    public override string EnglishName => "AholoImportMesh";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DImportMeshCommand.Execute(doc,mode);
}

[Guid("4949550b-a146-5735-9561-cc35eae22d8d")]
public sealed class LegacyAholoImportPlyCommand : Command
{
    public override string EnglishName => "AholoImportPly";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DImportPlyCommand.Execute(doc,mode);
}

[Guid("e9254f11-eea0-5c25-aefb-9017aabb30d3")]
public sealed class LegacyAholoImportSceneCommand : Command
{
    public override string EnglishName => "AholoImportScene";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DImportSceneCommand.Execute(doc,mode);
}

[Guid("f5111096-7a38-562c-a910-7a4c51419b79")]
public sealed class LegacyAholoModelCommand : Command
{
    public override string EnglishName => "AholoModel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DModelCommand.Execute(doc,mode);
}

[Guid("0e6c8245-8392-5e0b-b59b-ec5f29b91c2f")]
public sealed class LegacyAholoPanelCommand : Command
{
    public override string EnglishName => "AholoPanel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DPanelCommand.Execute(doc,mode);
}

[Guid("afd8c4c5-6ed5-526c-a42c-0b664a16d34a")]
public sealed class LegacyAholoPlaceTaskCommand : Command
{
    public override string EnglishName => "AholoPlaceTask";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DPlaceTaskCommand.Execute(doc,mode);
}

[Guid("45102068-d0de-52da-9c76-ead0c49e6228")]
public sealed class LegacyAholoRenderCommand : Command
{
    public override string EnglishName => "AholoRender";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DRenderCommand.Execute(doc,mode);
}

[Guid("7ca17abe-e0ba-590a-bf7a-226e7a1988bd")]
public sealed class LegacyAholoTasksCommand : Command
{
    public override string EnglishName => "AholoTasks";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DTasksCommand.Execute(doc,mode);
}

[Guid("b31bd269-7fcf-587b-a4df-af3cdf94961d")]
public sealed class LegacyAholoWorldCommand : Command
{
    public override string EnglishName => "AholoWorld";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Aholo3DWorldCommand.Execute(doc,mode);
}
