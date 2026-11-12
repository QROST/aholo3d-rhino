using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Aholo3D.Tests;

public sealed class RhinoPluginMetadataTests
{
    private static readonly Guid ExpectedPluginId =
        new("D1D17217-1169-41BB-B63A-FEE17DFA777F");

    [Fact]
    public void BuiltRhinoPluginCarriesStableAssemblyGuid()
    {
        using FileStream stream = File.OpenRead(GetPluginPath());
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        CustomAttribute attribute = Assert.Single(
            GetAttributes(metadata, "System.Runtime.InteropServices", "GuidAttribute"));
        BlobReader value = metadata.GetBlobReader(attribute.Value);

        Assert.Equal((ushort)1, value.ReadUInt16());
        string? guid = value.ReadSerializedString();
        Assert.NotNull(guid);
        Assert.Equal(ExpectedPluginId, Guid.Parse(guid));
        Assert.Equal((ushort)0, value.ReadUInt16());
        Assert.Equal(0, value.RemainingBytes);
    }

    [Theory]
    // RhinoCommon 8.32 DescriptionType: Organization = 0, WebSite = 4.
    // Keep this test independent of the host SDK at runtime.
    [InlineData(0, "CuriousArc")]
    [InlineData(4, "https://github.com/QROST/aholo3d-rhino")]
    public void BuiltRhinoPluginCarriesPublisherMetadata(int descriptionType, string expected)
    {
        using FileStream stream = File.OpenRead(GetPluginPath());
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        List<(int Type, string? Text)> descriptions = [];
        foreach (CustomAttribute attribute in GetAttributes(
            metadata, "Rhino.PlugIns", "PlugInDescriptionAttribute"))
        {
            BlobReader value = metadata.GetBlobReader(attribute.Value);
            Assert.Equal((ushort)1, value.ReadUInt16());
            descriptions.Add((value.ReadInt32(), value.ReadSerializedString()));
            Assert.Equal((ushort)0, value.ReadUInt16());
            Assert.Equal(0, value.RemainingBytes);
        }

        Assert.Equal(expected, Assert.Single(descriptions, item => item.Type == descriptionType).Text);
    }

    [Fact]
    public void BuiltCommandsHaveUniqueNamesAndGuidsWithoutLoadingRhino()
    {
        using var stream=File.OpenRead(GetPluginPath());using var pe=new PEReader(stream);var metadata=pe.GetMetadataReader();
        var commands=new Dictionary<string,Guid>(StringComparer.OrdinalIgnoreCase);
        foreach(var handle in metadata.TypeDefinitions)
        {
            var definition=metadata.GetTypeDefinition(handle);
            if(definition.BaseType.Kind!=HandleKind.TypeReference)continue;
            var parent=metadata.GetTypeReference((TypeReferenceHandle)definition.BaseType);
            if(!metadata.StringComparer.Equals(parent.Namespace,"Rhino.Commands")||!metadata.StringComparer.Equals(parent.Name,"Command"))continue;
            var getter=Assert.Single(definition.GetMethods().Select(metadata.GetMethodDefinition),m=>metadata.StringComparer.Equals(m.Name,"get_EnglishName"));
            var il=pe.GetMethodBody(getter.RelativeVirtualAddress).GetILBytes()!;
            // Existing command names are literal getters: ldstr <token>; ret.
            Assert.Equal(6,il.Length);Assert.Equal(0x72,il[0]);Assert.Equal(0x2a,il[5]);
            int token=System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(il.AsSpan(1,4));
            Assert.Equal(0x70,token>>24);
            string name=metadata.GetUserString(System.Reflection.Metadata.Ecma335.MetadataTokens.UserStringHandle(token&0xffffff));
            var guids=new List<Guid>();
            foreach(var attributeHandle in definition.GetCustomAttributes())
            {
                var attribute=metadata.GetCustomAttribute(attributeHandle);
                if(attribute.Constructor.Kind!=HandleKind.MemberReference)continue;
                var constructor=metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if(constructor.Parent.Kind!=HandleKind.TypeReference)continue;
                var type=metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if(!metadata.StringComparer.Equals(type.Namespace,"System.Runtime.InteropServices")||!metadata.StringComparer.Equals(type.Name,"GuidAttribute"))continue;
                var blob=metadata.GetBlobReader(attribute.Value);Assert.Equal((ushort)1,blob.ReadUInt16());guids.Add(Guid.Parse(blob.ReadSerializedString()!));
            }
            var guid=Assert.Single(guids);Assert.NotEqual(Guid.Empty,guid);Assert.True(commands.TryAdd(name,guid),"Duplicate command name: "+name);
        }
        Assert.Equal(commands.Count,commands.Values.Distinct().Count());
        Assert.Equal(new Guid("30BE962C-4333-419C-BFB1-58D4E7C04DE7"),commands["Aholo3DWorld"]);
        Assert.Equal(new Guid("2CFAB6F4-2A45-41E5-BD0B-CB48BFA42067"),commands["Aholo3DModel"]);
        Assert.Equal(new Guid("4A2AD212-2088-4236-869F-EDC4F9EC44BD"),commands["Aholo3DTasks"]);
        Assert.Equal(new Guid("3E5B6073-3659-40D4-82D2-E1A03E7B1CF4"),commands["Aholo3DRender"]);
        Assert.Equal(new Guid("4E7AB6C0-5922-4E79-9F28-0897985E7F5D"),commands["Aholo3DCancelOperation"]);
    }

    [Fact]
    public void LegacyCommandsDispatchToExactlyTheSameImplementation()
    {
        using var stream=File.OpenRead(GetPluginPath());using var pe=new PEReader(stream);var metadata=pe.GetMetadataReader();
        var bodies=new Dictionary<string,byte[]>();
        foreach(var handle in metadata.TypeDefinitions)
        {
            var type=metadata.GetTypeDefinition(handle);
            var methods=type.GetMethods().Select(metadata.GetMethodDefinition).Where(m=>metadata.StringComparer.Equals(m.Name,"RunCommand")).ToArray();
            if(methods.Length==0)continue;
            var method=Assert.Single(methods);
            bodies[metadata.GetString(type.Name)]=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        }
        var legacy=bodies.Where(p=>p.Key.StartsWith("LegacyAholo",StringComparison.Ordinal)).ToArray();
        Assert.Equal(18,legacy.Length);
        foreach(var entry in legacy)
        {
            string canonical=entry.Key.Replace("LegacyAholo","Aholo3D",StringComparison.Ordinal);
            Assert.Equal(bodies[canonical],entry.Value);
        }
    }

    // Inspect the compiled PE without loading the plug-in or resolving RhinoCommon.
    // Runtime reflection can resolve unrelated custom attribute types even when
    // requesting only GuidAttribute, so it is unsuitable for this portable test.
    private static IEnumerable<CustomAttribute> GetAttributes(
        MetadataReader metadata, string typeNamespace, string typeName)
    {
        foreach (CustomAttributeHandle handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            MemberReference constructor = metadata.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.StringComparer.Equals(type.Namespace, typeNamespace) &&
                metadata.StringComparer.Equals(type.Name, typeName))
            {
                yield return attribute;
            }
        }
    }

    private static string GetPluginPath()
    {
        string root = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                ".."));
        string configuration = Directory
            .GetParent(
                AppContext.BaseDirectory.TrimEnd(
                    Path.DirectorySeparatorChar))!
            .Name;
        string pluginPath = Path.Combine(
            root,
            "src",
            "Aholo3D.Rhino",
            "bin",
            configuration,
            "net7.0",
            "Aholo3D.Rhino.rhp");
        Assert.True(
            File.Exists(pluginPath),
            $"The Rhino plug-in build output was not found at {pluginPath}.");

        return pluginPath;
    }
}
