using System.Text.Json;
using Aholo3D.Core;

// Offline protocol oracle. No account storage, assets, child services, or network access.
internal static class RenderRpcFixture
{
    public static async Task<int> RunAsync(bool stall)
    {
        while(await BoundedLine.ReadAsync(Console.In,CancellationToken.None) is { } line)
        {
            var request=JsonSerializer.Deserialize<RpcRequest>(line,Aholo3DJson.Options)!;
            if(request.Version!=1)return 2;
            if(request.Method=="connection")
            {
                var connection=new ConnectionState(true,"fixture-account",false,"fixture-epoch");
                Console.WriteLine(JsonSerializer.Serialize(new RpcResponse(1,request.Id,Aholo3DJson.Element(connection),null),Aholo3DJson.Options));
                continue;
            }
            if(request.Method is not ("render" or "continueRender"))return 2;
            if(stall)await Task.Delay(Timeout.Infinite);
            var render=request.Payload.Deserialize<RenderRequest>(Aholo3DJson.Options)!;
            var task=new LocalTask{Kind="render",Name=request.CredentialEpoch??"missing",Render=render};
            Console.WriteLine(JsonSerializer.Serialize(new RpcResponse(1,request.Id,Aholo3DJson.Element(task),null),Aholo3DJson.Options));
        }
        return 0;
    }
}
