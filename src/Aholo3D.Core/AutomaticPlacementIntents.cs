namespace Aholo3D.Core;

public sealed record AutomaticPlacementIntent<T>(T Target, string ConnectionId, string? Epoch, long Generation);

/// <summary>Volatile authority from the original button click; never reconstructed from journals.</summary>
public sealed class AutomaticPlacementIntents<T>
{
    private long generation;
    private readonly Dictionary<Guid,AutomaticPlacementIntent<T>> pending=new();
    public int Count=>pending.Count;
    public AutomaticPlacementIntent<T> Capture(T target,ConnectionState connection)
    {
        if(!connection.Connected||string.IsNullOrEmpty(connection.ConnectionId))throw new InvalidOperationException("Connect before capturing automatic placement.");
        return new(target,connection.ConnectionId,connection.CredentialEpoch,generation);
    }
    public bool Attach(LocalTask task,AutomaticPlacementIntent<T> intent)
    {
        if(intent.Generation!=generation||intent.ConnectionId!=task.ConnectionId||task.Kind!="object"||task.LuxTaskId is null||
            !MeshPlacementPolicy.SupportsAutomaticPlacement(task.ServiceOperation??"")||task.Status is not ("PENDING" or "RUNNING" or "SUCCEEDED"))return false;
        return pending.TryAdd(task.Id,intent);
    }
    public bool TryGet(Guid id,ConnectionState connection,out AutomaticPlacementIntent<T>? intent)
    {
        if(pending.TryGetValue(id,out var value)&&connection.Connected&&value.Generation==generation&&value.ConnectionId==connection.ConnectionId&&value.Epoch==connection.CredentialEpoch){intent=value;return true;}
        intent=null;return false;
    }
    public bool IsCurrent(Guid id,AutomaticPlacementIntent<T> intent,ConnectionState connection)
        =>TryGet(id,connection,out var current)&&ReferenceEquals(current,intent);
    public void Remove(Guid id)=>pending.Remove(id);
    public void RevokeAll(){generation++;pending.Clear();}
}
