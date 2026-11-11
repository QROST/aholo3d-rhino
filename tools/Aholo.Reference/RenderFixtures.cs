using Aholo.Core;

internal static class RenderFixtures
{
    internal static int Run(string output)
    {
        string directory=Path.GetFullPath(output);Directory.CreateDirectory(directory);
        var camera=new RenderCamera(new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,3000,1},35,36,20.25,.1,100000);
        var light=new RenderLight("Sphere",new[]{1d,0,0,0,0,1,0,0,0,0,1,0,1000,2000,3000,1},new[]{1d,.5,.25},2,Radius:50);
        var start=new RealtimeStartRequest(Guid.Parse("c50416b3-028c-4e65-a29e-687d81952c33"),"fixture",camera,Lights:new[]{light});
        void Write(string name,string content)=>File.WriteAllText(Path.Combine(directory,name),content);
        Write("base.usda",RealtimeScene.Build(start));
        Write("patch.usda",RealtimeScene.ScenePatch(start,new(start.Id,1,new(10,20,30,2,90),.8,new[]{light with{Intensity=8}})));
        Write("camera.usda",RealtimeScene.CameraPatch(start with{Camera=camera with{Transform=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,400,500,600,1}}}));
        Write("gs.usda",RealtimeScene.ScenePatch(start with{Gaussian=true,GltfCoordinates=false},new(start.Id,1,new(1,2,3),1,new[]{light})));
        var initial=new RealtimeSceneLayout(new[]{new RealtimeAssetSlot("Model","mesh-a",Model:new(10,20,30))},Array.Empty<RealtimeLightSlot>());
        var multiple=initial with{Assets=initial.Assets.Append(new RealtimeAssetSlot("Model_gs","gaussian-b",true,false,new(100,200,300,2,90))).ToArray(),
            Lights=new[]{new RealtimeLightSlot("Light_key",light),new RealtimeLightSlot("Light_fill",light with{Kind="Rect",Width=500,Height=700,Intensity=4})}};
        var adjusted=multiple with{Assets=new[]{multiple.Assets[0] with{Model=new(-10,-20,40,.5,180)},multiple.Assets[1]},
            Lights=new[]{multiple.Lights[0] with{Light=light with{Transform=new[]{1d,0,0,0,0,1,0,0,0,0,1,0,-100,-200,500,1},Radius=75,Intensity=3}},multiple.Lights[1]},EnvironmentIntensity=.25};
        var removed=adjusted with{Assets=new[]{adjusted.Assets[1]},Lights=new[]{adjusted.Lights[0]}};
        Write("composition-base.usda",RealtimeScene.Build(start with{Scene=initial}));
        Write("composition-add.usda",RealtimeLayout.Patch(initial,multiple));
        Write("composition-edit.usda",RealtimeLayout.Patch(multiple,adjusted));
        Write("composition-remove.usda",RealtimeLayout.Patch(adjusted,removed));
        Write("composition-empty.usda",RealtimeLayout.Patch(removed,new(Array.Empty<RealtimeAssetSlot>(),Array.Empty<RealtimeLightSlot>())));
        Console.WriteLine("Production render/patch fixtures written; no service or Rhino calls.");
        return 0;
    }
}
