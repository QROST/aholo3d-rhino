namespace Aholo3D.Core;

/// <summary>Rhino channel selection; a null channel denotes surface parameterization.</summary>
public static class TextureMappingSelection
{
    public static int? Select(int requested,bool surfaceParameters,IReadOnlyCollection<int> objectChannels,IReadOnlyCollection<int> parentChannels)
    {
        ArgumentNullException.ThrowIfNull(objectChannels);ArgumentNullException.ThrowIfNull(parentChannels);
        if(surfaceParameters)return null;
        if(requested==0)throw new InvalidOperationException(UiText.Get("This bitmap uses legacy mapping channel 0. Reassign it to a current mapping channel in Rhino before export."));
        int channel=requested;
        if(objectChannels.Contains(channel)||parentChannels.Contains(channel))return RequireLocal(channel,objectChannels,parentChannels);
        if(channel!=1&&(objectChannels.Contains(1)||parentChannels.Contains(1)))return RequireLocal(1,objectChannels,parentChannels);
        return null;
    }
    private static int RequireLocal(int channel,IReadOnlyCollection<int> own,IReadOnlyCollection<int> parents)
    {
        if(parents.Contains(channel))throw new InvalidOperationException(UiText.Get("The selected bitmap mapping is assigned to a parent block. Apply it to the geometry itself or disable bitmap export."));
        if(!own.Contains(channel))throw new InvalidOperationException("The selected object mapping is unavailable.");
        return channel;
    }
}

/// <summary>Apply bitmap affine UV placement once, then change bottom-left Rhino UVs to glTF top-left UVs.</summary>
public readonly record struct TextureUvTransform(double UU,double UV,double UOffset,double VU,double VV,double VOffset)
{
    public static TextureUvTransform Identity=>new(1,0,0,0,1,0);
    public static TextureUvTransform FromMatrix(IReadOnlyList<double> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if(m.Count!=16||m.Any(v=>!double.IsFinite(v))||new[]{2,6,8,9,11,12,13,14}.Any(i=>m[i]!=0)||m[15]!=1)
            throw new ArgumentException("Bitmap UV placement must be a finite two-dimensional affine transform.");
        return new(m[0],m[1],m[3],m[4],m[5],m[7]);
    }
    public float[] ToGltf(float[] source,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(source);ct.ThrowIfCancellationRequested();
        if(source.Length==0||source.Length%2!=0)throw new ArgumentException("Texture coordinates must be nonempty pairs.");
        if(!double.IsFinite(UU)||!double.IsFinite(UV)||!double.IsFinite(UOffset)||!double.IsFinite(VU)||!double.IsFinite(VV)||!double.IsFinite(VOffset))throw new ArgumentException("The bitmap UV transform is not finite.");
        var result=new float[source.Length];
        for(int i=0;i<source.Length;i+=2)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();
            double u=source[i],v=source[i+1];
            if(!double.IsFinite(u)||!double.IsFinite(v))throw Invalid();
            float x=(float)(UU*u+UV*v+UOffset),y=(float)(1-(VU*u+VV*v+VOffset));
            if(!float.IsFinite(x)||!float.IsFinite(y))throw Invalid();
            result[i]=x;result[i+1]=y;
        }
        ct.ThrowIfCancellationRequested();return result;
    }
    private static InvalidDataException Invalid()=>new(UiText.Get("The bitmap coordinates are invalid or exceed the output format range. Check its mapping and repeat settings."));
}
