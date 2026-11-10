using System.Security.Cryptography;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class NativeMeshSnapshotTests : IDisposable
{
    private readonly string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aholo-mesh-tests-"+Guid.NewGuid());
    private string File(string content,string extension=".ply")
    {Directory.CreateDirectory(root);string path=System.IO.Path.Combine(root,Guid.NewGuid()+extension);System.IO.File.WriteAllText(path,content);return path;}
    private const string Vertices="element vertex 4\nproperty float x\nproperty float y\nproperty float z\n";
    private const string Faces="element face 1\nproperty list uchar int vertex_indices\n";
    private const string Positions="0 0 0\n1 0 0\n1 1 1\n0 1 0\n";
    private static string Mesh(string face="4 0 1 2 3")=>"ply\nformat ascii 1.0\n"+Vertices+Faces+"end_header\n"+Positions+face+"\n";
    [Fact]
    public void MeshSnapshotIsPrivateImmutableAndKeepsNormalizedSuffix()
    {
        string source=File(Mesh(),".PLY");byte[] bytes=System.IO.File.ReadAllBytes(source);
        using var snapshot=NativeMeshImportSnapshot.Create(source,Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.EndsWith(".ply",snapshot.Path);Assert.Equal(bytes,System.IO.File.ReadAllBytes(snapshot.Path));
        System.IO.File.WriteAllText(source,"changed");Assert.Equal(bytes,System.IO.File.ReadAllBytes(snapshot.Path));
        if(!OperatingSystem.IsWindows())Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,System.IO.File.GetUnixFileMode(snapshot.Path));
        snapshot.Dispose();snapshot.Dispose();Assert.False(System.IO.File.Exists(snapshot.Path));Assert.Equal("changed",System.IO.File.ReadAllText(source));
    }
    [Fact]
    public void PolygonMeshIsSupportedWithoutAnArtificialTriangleOnlyRestriction()
    {var result=PlyReader.ValidateMesh(File(Mesh()));Assert.Equal(PlyContentKind.Mesh,result.Kind);Assert.Equal(4,result.Vertices);Assert.Equal(1,result.Faces);}
    [Theory]
    [InlineData("3 0 1 4")]
    [InlineData("3 0 -1 2")]
    [InlineData("2 0 1")]
    [InlineData("3 0 1")]
    [InlineData("3 0 1 2 unexpected")]
    public void InvalidOrIncompleteTopologyIsRejected(string face)=>Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(File(Mesh(face)),null));
    [Fact]
    public void PointsPartialGsAndAmbiguousTopologyNeverBecomeOrdinaryMeshes()
    {
        string points=File("ply\nformat ascii 1.0\n"+Vertices+"end_header\n"+Positions);
        Assert.Equal(PlyContentKind.Points,PlyReader.Inspect(points).Kind);Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(points,null));
        string partial=File("ply\nformat ascii 1.0\n"+Vertices+"property float opacity\n"+Faces+"end_header\n");
        Assert.Equal(PlyContentKind.Unsupported,PlyReader.Inspect(partial).Kind);
        string fields=string.Concat(new[]{"opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3","f_dc_0","f_dc_1","f_dc_2"}.Select(n=>"property float "+n+"\n"));
        string gs=File("ply\nformat binary_little_endian 1.0\n"+Vertices+fields+"end_header\n");
        Assert.Equal(PlyContentKind.Gaussian,PlyReader.Inspect(gs).Kind);Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(gs,null));
        Assert.Equal(PlyContentKind.Unsupported,PlyReader.Inspect(File("ply\nformat ascii 1.0\n"+Vertices+fields+Faces+"end_header\n")).Kind);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothBinaryByteOrdersValidateTopology(bool big)
    {
        string path=File("ply\nformat binary_"+(big?"big":"little")+"_endian 1.0\n"+Vertices+Faces+"end_header\n");
        using(var output=new FileStream(path,FileMode.Append))
        {
            void Bytes(byte[] value){if(big)Array.Reverse(value);output.Write(value);}
            foreach(float n in new float[]{0,0,0,1,0,0,1,1,1,0,1,0})Bytes(BitConverter.GetBytes(n));
            output.WriteByte(4);foreach(int index in new[]{0,1,2,3})Bytes(BitConverter.GetBytes(index));
        }
        Assert.Equal(PlyContentKind.Mesh,PlyReader.ValidateMesh(path).Kind);
    }
    [Fact]
    public void HashMismatchCancellationAndNonfiniteValuesLeaveSourceIntact()
    {
        string source=File(Mesh());Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(source,new string('0',64)));
        using var cancel=new CancellationTokenSource();cancel.Cancel();Assert.Throws<OperationCanceledException>(()=>NativeMeshImportSnapshot.Create(source,null,ct:cancel.Token));
        Assert.Equal(Mesh(),System.IO.File.ReadAllText(source));
        Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(File(Mesh().Replace("0 0 0\n","NaN 0 0\n")),null));
    }
    [Fact]
    public void SnapshotMutationIsRejectedByTheNativePreflightHashGuard()
    {
        using var snapshot=NativeMeshImportSnapshot.Create(File(Mesh()),null);
        NativeMeshImportSnapshot.Verify(snapshot.Path,snapshot.Sha256);
        System.IO.File.WriteAllText(snapshot.Path,Mesh("3 0 1 2"));
        Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Verify(snapshot.Path,snapshot.Sha256));
    }
    [Fact]
    public void UnsupportedSuffixIsNotHandedToANativeImporter()=>Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(File("anything",".fbx"),null));
    [Fact]
    public void StlSnapshotPreservesOriginalBytesForNativePreflight()
    {
        const string stl="solid sample\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid sample\n";
        using var snapshot=NativeMeshImportSnapshot.Create(File(stl,".stl"),null);Assert.EndsWith(".stl",snapshot.Path);Assert.Equal(stl,System.IO.File.ReadAllText(snapshot.Path));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
