using System.Buffers.Binary;
using System.Text.Json;
namespace Aholo.Core;
/// <summary>Upload container preflight, including animated GLBs. Native static placement has separate validation.</summary>
public static class GlbEnvelope
{
    public static void Validate(string path)
    {
        using var file=File.OpenRead(path);Span<byte> header=stackalloc byte[12];
        if(file.Length<24||file.Read(header)!=12||!header[..4].SequenceEqual("glTF"u8)||BinaryPrimitives.ReadUInt32LittleEndian(header[4..])!=2||BinaryPrimitives.ReadUInt32LittleEndian(header[8..])!=file.Length)throw Invalid();
        Span<byte> chunk=stackalloc byte[8];int index=0;bool binary=false;
        while(file.Position<file.Length)
        {
            if(file.Read(chunk)!=8)throw Invalid();uint size=BinaryPrimitives.ReadUInt32LittleEndian(chunk),type=BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if(size==0||size%4!=0||size>file.Length-file.Position)throw Invalid();
            if(index++==0)
            {
                if(type!=0x4E4F534A||size>int.MaxValue)throw Invalid();
                byte[] json=new byte[(int)size];file.ReadExactly(json);
                try{using var document=JsonDocument.Parse(json);if(document.RootElement.ValueKind!=JsonValueKind.Object||!document.RootElement.TryGetProperty("asset",out var asset)||asset.ValueKind!=JsonValueKind.Object||!asset.TryGetProperty("version",out var version)||version.ValueKind!=JsonValueKind.String||!version.GetString()!.StartsWith("2.",StringComparison.Ordinal))throw Invalid();}
                catch(JsonException){throw Invalid();}
            }
            else {if(type!=0x004E4942||binary)throw Invalid();binary=true;file.Position+=size;}
        }
    }
    private static ArgumentException Invalid()=>new("The input GLB container is incomplete or unsupported. No file was uploaded.");
}
