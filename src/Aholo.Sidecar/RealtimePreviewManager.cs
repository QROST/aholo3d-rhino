using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Sidecar;

/// <summary>Owns paid session intent, a single latest frame, and durable server cleanup obligations.</summary>
public sealed class RealtimePreviewManager
{
    private sealed class Entry(RealtimePreviewRecord record,RealtimeStartRequest start,IRealtimePreviewBackend backend,FileStream lease)
    {
        internal readonly object Gate=new();
        internal RealtimePreviewRecord Record=record;
        internal readonly RealtimeStartRequest Start=start;
        internal readonly IRealtimePreviewBackend Backend=backend;
        internal readonly CancellationTokenSource Cancel=new();
        internal FileStream? Lease=lease;
        internal Task Worker=Task.CompletedTask,CameraWorker=Task.CompletedTask;
        internal byte[]? Frame;
        internal DateTimeOffset? Received;
        internal long Sequence,AcceptedCamera,AcknowledgedCamera;
        internal (long Revision,string Content)? PendingCamera;
        internal bool Initialized,CameraRunning;
    }
    private readonly Dictionary<Guid,Entry> entries=new();
    private readonly object gate=new();
    private readonly SemaphoreSlim lifecycle=new(1);
    private readonly CredentialCoordinator credentials;
    private readonly RealtimeJournal journal;
    private readonly Func<ApiKeyCredential,string,IRealtimePreviewBackend> backendFactory;
    private readonly TimeSpan pollInterval;
    private bool stopped;
    public RealtimePreviewManager(CredentialCoordinator credentials,RealtimeJournal journal,
        Func<ApiKeyCredential,string,IRealtimePreviewBackend> backendFactory,TimeSpan? pollInterval=null)
    {this.credentials=credentials;this.journal=journal;this.backendFactory=backendFactory;this.pollInterval=pollInterval??TimeSpan.FromSeconds(2);}

    public async Task<RealtimePreviewPacket> StartAsync(RealtimeStartRequest request,string? epoch,CancellationToken ct)
    {
        if(request.Id==Guid.Empty)throw new ArgumentException("A preview identity is required.");
        string usda=RealtimeScene.Build(request);
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if(stopped)throw new InvalidOperationException("Realtime control has stopped.");
            var key=await credentials.RequireAsync(epoch,ct).ConfigureAwait(false);
            lock(gate)
            {
                if(entries.TryGetValue(request.Id,out var existing))return Packet(existing,0);
                if(entries.Values.Any(e=>!e.Worker.IsCompleted))throw new InvalidOperationException("Stop the active cloud preview before starting another.");
            }
            using var startAdmission=journal.LockAdmission();
            if(journal.Pending().Any(r=>r.ConnectionId==key.ConnectionId))
                throw new InvalidOperationException("Resolve pending cloud preview cleanup before starting another paid session.");
            using var admission=await credentials.AcquireRequestAsync(epoch,ct).ConfigureAwait(false);
            var lease=journal.Lock(request.Id);
            try
            {
                if(journal.Exists(request.Id))throw new InvalidOperationException("This preview already has a journal. Inspect or close the original session; it will not be created again.");
                var record=new RealtimePreviewRecord{Id=request.Id,ConnectionId=key.ConnectionId,Epoch=epoch!,MeshId=request.MeshId};
                var entry=new Entry(record,request,backendFactory(key,epoch!),lease);
                journal.Save(record); // Paid creation must never precede its durable intent.
                lock(gate){entries.Add(request.Id,entry);entry.Worker=Task.Run(()=>RunAsync(entry,usda));}
                return Packet(entry,0);
            }
            catch{lease.Dispose();throw;}
        }
        finally{lifecycle.Release();}
    }
    private async Task RunAsync(Entry entry,string usda)
    {
        Task monitor=MonitorCredentialsAsync(entry);
        try
        {
            if(entry.Cancel.IsCancellationRequested)throw new RequestNotSentCanceledException(entry.Cancel.Token);
            var operation=await entry.Backend.CreateAsync(entry.Record.Id.ToString("N"),usda,entry.Cancel.Token).ConfigureAwait(false);
            Set(entry,r=>r with{SessionId=operation.OperationId,Stage="Connecting"});
            using var initialization=CancellationTokenSource.CreateLinkedTokenSource(entry.Cancel.Token);
            initialization.CancelAfter(TimeSpan.FromSeconds(120));
            while(!operation.Done)
            {
                await Task.Delay(pollInterval,initialization.Token).ConfigureAwait(false);
                operation=await entry.Backend.QueryAsync(operation.OperationId,initialization.Token).ConfigureAwait(false);
            }
            if(operation.Status!=RealtimeSessionStatus.Active)throw new InvalidOperationException("The realtime session did not become active.");
            await entry.Backend.ConnectAsync(operation,initialization.Token).ConfigureAwait(false);
            while(true)
            {
                var message=await entry.Backend.ReadAsync(entry.Initialized?entry.Cancel.Token:initialization.Token).ConfigureAwait(false);
                switch(message)
                {
                    case RealtimeJpegFrame frame:
                        lock(entry.Gate){entry.Frame=frame.Bytes;entry.Sequence++;entry.Received=DateTimeOffset.UtcNow;}
                        break;
                    case RealtimeStatusMessage{Code:RealtimeStatusCode.Initialized}:
                        lock(entry.Gate){entry.Initialized=true;StartCameraWorker(entry);}
                        Set(entry,r=>r with{Stage="Streaming",Error=null});break;
                    case RealtimeStatusMessage{Code:RealtimeStatusCode.RenderError or RealtimeStatusCode.EngineError}:
                        Set(entry,r=>r with{Error="The cloud renderer reported an error. The session remains open; stop it when finished."});break;
                    case RealtimeStatusMessage{Code:RealtimeStatusCode.NodeOverloaded or RealtimeStatusCode.InitializationFailed or RealtimeStatusCode.SlaveLimitExceeded}:
                        throw new InvalidOperationException("Realtime streaming initialization failed.");
                    case RealtimeStreamClosed:
                        throw new InvalidOperationException("The realtime stream closed.");
                }
            }
        }
        catch(Exception error)
        {
            bool knownNotSent=error is RequestNotSentCanceledException||error is ProviderException{SubmissionMayHaveSucceeded:false};
            TrySet(entry,r=>r with{Stage=r.SessionId is null?(knownNotSent?"NotSubmitted":"SubmissionUnknown"):"Stopping",
                NeedsCleanup=r.SessionId is not null||!knownNotSent,
                Error=error is OperationCanceledException?"Realtime waiting stopped. Check cleanup status before starting again.":SafeError(error)});
        }
        finally
        {
            entry.Cancel.Cancel();
            lock(entry.Gate)entry.Initialized=false;
            try{await monitor.ConfigureAwait(false);}catch{}
            if(entry.Record.SessionId is{} sessionId)
            {
                try{await entry.Backend.CloseAsync(sessionId,false).ConfigureAwait(false);TrySet(entry,r=>r with{NeedsCleanup=false,Stage="Closed"});}
                catch(Exception){TrySet(entry,r=>r with{NeedsCleanup=true,Stage="CleanupRequired",Error="Server cleanup is not confirmed. Retry closing the original session."});}
            }
            try{await entry.CameraWorker.ConfigureAwait(false);}catch{}
            lock(entry.Gate){entry.Frame=null;entry.PendingCamera=null;}
            entry.Lease?.Dispose();entry.Lease=null;
        }
    }
    private async Task MonitorCredentialsAsync(Entry entry)
    {
        try
        {
            while(true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2),entry.Cancel.Token).ConfigureAwait(false);
                _=await credentials.RequireAsync(entry.Record.Epoch,entry.Cancel.Token).ConfigureAwait(false);
            }
        }
        catch(OperationCanceledException)when(entry.Cancel.IsCancellationRequested){}
        catch{TrySet(entry,r=>r with{Error="The preview connection changed. Server cleanup may require restoring the original account."});entry.Cancel.Cancel();}
    }
    public RealtimePreviewPacket Get(Guid id,long afterFrame)
    {
        Entry? entry;lock(gate)entries.TryGetValue(id,out entry);
        return entry is null?new(new(State:new(journal.Read(id)))):Packet(entry,afterFrame);
    }
    public RealtimePreviewPacket Pending()=>new(new(Records:journal.Pending()));
    public async Task<RealtimePreviewPacket> CameraAsync(RealtimeCameraRequest request,string? epoch,CancellationToken ct)
    {
        var key=await credentials.RequireAsync(epoch,ct).ConfigureAwait(false);
        Entry entry;lock(gate)entry=entries.GetValueOrDefault(request.Id)??throw new InvalidOperationException("The original preview is not active.");
        if(entry.Record.Epoch!=epoch||entry.Record.ConnectionId!=key.ConnectionId)throw new InvalidOperationException("The preview belongs to the original connection.");
        string content=RealtimeScene.CameraPatch(entry.Start with{Camera=request.Camera});
        lock(entry.Gate)
        {
            if(entry.Worker.IsCompleted||entry.Cancel.IsCancellationRequested)throw new InvalidOperationException("The original preview is closing or closed.");
            if(request.Revision>entry.AcceptedCamera)
            {entry.AcceptedCamera=request.Revision;entry.PendingCamera=(request.Revision,content);StartCameraWorker(entry);}
        }
        return Packet(entry,long.MaxValue);
    }
    private void StartCameraWorker(Entry entry)
    {
        if(!entry.Initialized||entry.CameraRunning||entry.PendingCamera is null)return;
        entry.CameraRunning=true;entry.CameraWorker=Task.Run(async()=>
        {
            try
            {
                while(true)
                {
                    (long Revision,string Content) patch;
                    lock(entry.Gate)
                    {
                        if(!entry.Initialized||entry.PendingCamera is null){entry.CameraRunning=false;return;}
                        patch=entry.PendingCamera.Value;entry.PendingCamera=null;
                    }
                    await entry.Backend.CameraAsync(patch.Content,entry.Cancel.Token).ConfigureAwait(false);
                    lock(entry.Gate)entry.AcknowledgedCamera=Math.Max(entry.AcknowledgedCamera,patch.Revision);
                }
            }
            catch(Exception error){TrySet(entry,r=>r with{Error=SafeError(error)});entry.Cancel.Cancel();lock(entry.Gate)entry.CameraRunning=false;}
        });
    }
    public async Task<RealtimePreviewPacket> StopAsync(Guid id,string? epoch,CancellationToken ct)
    {
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Entry? entry;lock(gate)entries.TryGetValue(id,out entry);
            if(entry is not null&&!entry.Worker.IsCompleted)
            {
                entry.Cancel.Cancel();await entry.Worker.WaitAsync(ct).ConfigureAwait(false);return Packet(entry,long.MaxValue);
            }
            using var lease=journal.Lock(id);
            if(!journal.Exists(id))
            {
                var originalKey=await credentials.RequireAsync(epoch,ct).ConfigureAwait(false);
                var tombstone=new RealtimePreviewRecord{Id=id,ConnectionId=originalKey.ConnectionId,Epoch=epoch!,Stage="NotSubmitted",NeedsCleanup=false,Error="Stopped before preview creation was admitted."};
                journal.Save(tombstone);return new(new(State:new(tombstone)));
            }
            var record=journal.Read(id);
            if(entry is not null&&entry.Record.SessionId is not null&&record.SessionId is null)record=entry.Record;
            if(!record.NeedsCleanup)return new(new(State:new(record)));
            if(record.SessionId is null)throw new InvalidOperationException("The create response was lost and the session may exist. Reconcile the original request on the official platform; creation will not be retried.");
            var key=await credentials.RequireAsync(epoch,ct).ConfigureAwait(false);
            if(key.ConnectionId!=record.ConnectionId)throw new InvalidOperationException("Restore the preview's original account before retrying server cleanup.");
            try
            {
                // Fresh explicit cleanup uses the reconnected original identity, never a different account.
                await backendFactory(key,epoch!).CloseAsync(record.SessionId,true).ConfigureAwait(false);
                record=record with{NeedsCleanup=false,Stage="Closed",Error=null,UpdatedAt=DateTimeOffset.UtcNow};journal.Save(record);
                if(entry is not null)lock(entry.Gate)entry.Record=record;
            }
            catch{if(entry is not null)TrySet(entry,r=>r with{NeedsCleanup=true,Stage="CleanupRequired"});throw;}
            return new(new(State:new(record)));
        }
        finally{lifecycle.Release();}
    }
    public async Task<RealtimePreviewPacket> ConfirmExternalResolutionAsync(Guid id,string? epoch,CancellationToken ct)
    {
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key=await credentials.RequireAsync(epoch,ct).ConfigureAwait(false);
            using var lease=journal.Lock(id);var record=journal.Read(id);
            if(record.ConnectionId!=key.ConnectionId||record.SessionId is not null||!record.NeedsCleanup)
                throw new InvalidOperationException("Only an unknown session from the original account can be marked externally resolved.");
            record=record with{NeedsCleanup=false,Stage="ExternallyResolved",Error="The user confirmed resolution on the official platform; no server close was verified by this plugin.",UpdatedAt=DateTimeOffset.UtcNow};
            journal.Save(record);lock(gate)if(entries.TryGetValue(id,out var entry))lock(entry.Gate)entry.Record=record;
            return new(new(State:new(record)));
        }
        finally{lifecycle.Release();}
    }
    public async Task<IDisposable> BeforeCredentialChangeAsync(CancellationToken ct)
    {
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try{await StopActiveAsync(ct).ConfigureAwait(false);return new Lease(lifecycle);}
        catch{lifecycle.Release();throw;}
    }
    public async Task ShutdownAsync(CancellationToken ct)
    {
        await lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try{stopped=true;await StopActiveAsync(ct).ConfigureAwait(false);}finally{lifecycle.Release();}
    }
    private async Task StopActiveAsync(CancellationToken ct)
    {
        Entry[] active;lock(gate)active=entries.Values.Where(e=>!e.Worker.IsCompleted).ToArray();
        foreach(var entry in active)entry.Cancel.Cancel();
        await Task.WhenAll(active.Select(e=>e.Worker)).WaitAsync(ct).ConfigureAwait(false);
    }
    private void Set(Entry entry,Func<RealtimePreviewRecord,RealtimePreviewRecord> change)
    {lock(entry.Gate){entry.Record=change(entry.Record) with{UpdatedAt=DateTimeOffset.UtcNow};journal.Save(entry.Record);}}
    private void TrySet(Entry entry,Func<RealtimePreviewRecord,RealtimePreviewRecord> change)
    {try{Set(entry,change);}catch(IOException){lock(entry.Gate)entry.Record=entry.Record with{Error="Realtime journal could not be saved. Retain the session ID and check server cleanup."};}catch(UnauthorizedAccessException){lock(entry.Gate)entry.Record=entry.Record with{Error="Realtime journal could not be saved. Retain the session ID and check server cleanup."};}}
    private static RealtimePreviewPacket Packet(Entry entry,long after)
    {lock(entry.Gate)return new(new(State:new(entry.Record,entry.Sequence,entry.Received,entry.AcknowledgedCamera)),entry.Sequence>after?entry.Frame:null);}
    private static string SafeError(Exception error)=>error is ProviderException or InvalidOperationException?error.Message:"Realtime preview failed. Check the original session and cleanup status.";
    private sealed class Lease(SemaphoreSlim gate):IDisposable{private int disposed;public void Dispose(){if(Interlocked.Exchange(ref disposed,1)==0)gate.Release();}}
}
