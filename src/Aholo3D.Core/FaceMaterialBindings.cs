namespace Aholo3D.Core;

/// <summary>Resolve source-face channels against the effective instance material, before tessellation.</summary>
public static class FaceMaterialBindings
{
    public static T[] Resolve<T>(IReadOnlyList<int> channels,T basis,Func<T,int,Guid> channelId,Func<Guid,T?> findMaterial,CancellationToken ct=default) where T:class
    {
        ArgumentNullException.ThrowIfNull(channels);ArgumentNullException.ThrowIfNull(basis);
        ArgumentNullException.ThrowIfNull(channelId);ArgumentNullException.ThrowIfNull(findMaterial);
        ct.ThrowIfCancellationRequested();
        var result=new T[channels.Count];var byChannel=new Dictionary<int,T>();var byId=new Dictionary<Guid,T>();
        for(int face=0;face<result.Length;face++)
        {
            ct.ThrowIfCancellationRequested();int channel=channels[face];
            if(channel<0)throw new InvalidDataException(UiText.Get("A face material channel is invalid. Repair its material assignment before export."));
            if(!byChannel.TryGetValue(channel,out var material))
            {
                Guid id=channel==0?Guid.Empty:channelId(basis,channel);
                if(id==Guid.Empty)material=basis;
                else if(!byId.TryGetValue(id,out material))
                {
                    material=findMaterial(id)??throw new InvalidDataException(UiText.Get("A face material is missing. Repair its material assignment before export."));
                    byId.Add(id,material);
                }
                byChannel.Add(channel,material);
            }
            result[face]=material;
        }
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
