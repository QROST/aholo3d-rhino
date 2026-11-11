namespace Aholo.Core;

/// <summary>A host snapshot of one instance reference; visibility already includes its own layer state.</summary>
public sealed record GaussianInstanceNode(Guid Id, Guid? Definition, GaussianTransform? Local, bool IsGaussian, bool Visible = true);
public sealed record GaussianInstanceOccurrence(string Path, Guid ObjectId, Guid RootId, GaussianTransform? World, bool IsTopLevel);
public sealed record GaussianInstanceResources(IReadOnlyList<Guid> ObjectIds,int CyclicDefinitions,int MissingDefinitions);
public sealed record GaussianInstanceGraphResult(IReadOnlyList<GaussianInstanceOccurrence> Occurrences, int CyclicDefinitions, int MissingDefinitions);

/// <summary>Expand repeated instances without merging occurrences or imposing a nesting/count limit.</summary>
public static class GaussianInstanceGraph
{
    private sealed record Link(Guid Id, Link? Parent)
    {
        internal string Key()
        {
            var ids=new Stack<string>();for(Link? p=this;p is not null;p=p.Parent)ids.Push(p.Id.ToString("N"));
            return string.Join("/",ids);
        }
    }
    private sealed record Frame(IEnumerator<GaussianInstanceNode> Items, GaussianTransform? Parent, Link? Path, Guid Root, Guid? Definition);
    public static GaussianInstanceGraphResult Expand(IEnumerable<GaussianInstanceNode> roots,
        Func<Guid,IReadOnlyList<GaussianInstanceNode>?> children,bool includeHidden=false,CancellationToken ct=default)
        =>Traverse(roots,children,includeHidden,false,ct);
    // Resource lifetime depends on reachable reference objects, not the potentially enormous number of copies.
    public static IReadOnlyList<Guid> FindResourceObjects(IEnumerable<GaussianInstanceNode> roots,
        Func<Guid,IReadOnlyList<GaussianInstanceNode>?> children,CancellationToken ct=default)
        =>FindResources(roots,children,ct).ObjectIds;
    public static GaussianInstanceResources FindResources(IEnumerable<GaussianInstanceNode> roots,
        Func<Guid,IReadOnlyList<GaussianInstanceNode>?> children,CancellationToken ct=default)
    {
        var graph=Traverse(roots,children,true,true,ct);
        return new(graph.Occurrences.Select(o=>o.ObjectId).ToArray(),graph.CyclicDefinitions,graph.MissingDefinitions);
    }
    private static GaussianInstanceGraphResult Traverse(IEnumerable<GaussianInstanceNode> roots,
        Func<Guid,IReadOnlyList<GaussianInstanceNode>?> children,bool includeHidden,bool resourcesOnly,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rootNodes=new List<GaussianInstanceNode>();foreach(var node in roots){ct.ThrowIfCancellationRequested();rootNodes.Add(node);}
        var definitions=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>?>();
        var parents=new Dictionary<Guid,HashSet<Guid>>();var visibleParents=new Dictionary<Guid,HashSet<Guid>>();var productive=new HashSet<Guid>();var pending=new Queue<Guid>();
        bool Included(GaussianInstanceNode node)=>includeHidden||node.Visible;
        int missing=0;
        foreach(var node in rootNodes.Where(Included))
            if(!node.IsGaussian){if(node.Definition is Guid id)pending.Enqueue(id);else missing++;}
        // Inspect each definition once, then propagate GS reachability backwards to a fixed point.
        // A shared GS-free DAG must cost O(definitions + edges), not the number of expanded copies.
        while(pending.TryDequeue(out var id))
        {
            ct.ThrowIfCancellationRequested();if(definitions.ContainsKey(id))continue;
            var source=children(id);var members=source?.ToArray();definitions[id]=members;
            if(members is null){missing++;continue;}
            foreach(var member in members)
            {
                ct.ThrowIfCancellationRequested();
                if(member.IsGaussian){if(Included(member))productive.Add(id);continue;}
                if(member.Definition is not Guid child){missing++;continue;}
                if(!parents.TryGetValue(child,out var ancestorsOfChild))parents.Add(child,ancestorsOfChild=new());
                ancestorsOfChild.Add(id);pending.Enqueue(child);
                if(Included(member))
                {
                    if(!visibleParents.TryGetValue(child,out var visibleAncestors))visibleParents.Add(child,visibleAncestors=new());
                    visibleAncestors.Add(id);
                }
            }
        }
        var cyclic=FindCyclicDefinitions(definitions,parents,ct);
        productive.ExceptWith(cyclic);
        foreach(var id in productive)pending.Enqueue(id);
        while(pending.TryDequeue(out var id))
        {
            ct.ThrowIfCancellationRequested();if(!visibleParents.TryGetValue(id,out var previous))continue;
            foreach(var parent in previous)if(!cyclic.Contains(parent)&&productive.Add(parent))pending.Enqueue(parent);
        }
        var resourceDefinitions=new HashSet<Guid>();var resourceObjects=new HashSet<Guid>();
        var result=new List<GaussianInstanceOccurrence>();var ancestors=new HashSet<Guid>();var frames=new Stack<Frame>();
        frames.Push(new(rootNodes.GetEnumerator(),GaussianTransform.Identity,null,Guid.Empty,null));
        try
        {
            while(frames.Count>0)
            {
                ct.ThrowIfCancellationRequested();var frame=frames.Peek();
                if(!frame.Items.MoveNext())
                {
                    frames.Pop();frame.Items.Dispose();if(frame.Definition is Guid complete)ancestors.Remove(complete);continue;
                }
                var node=frame.Items.Current;if(!includeHidden&&!node.Visible)continue;
                var path=new Link(node.Id,frame.Path);Guid root=frame.Path is null?node.Id:frame.Root;
                var world=Compose(frame.Parent,node.Local);
                if(node.IsGaussian)
                {
                    if(!resourcesOnly||resourceObjects.Add(node.Id))result.Add(new(path.Key(),node.Id,root,Renderable(world),frame.Path is null));
                    continue;
                }
                if(node.Definition is not Guid definition||!productive.Contains(definition))continue;
                if(ancestors.Contains(definition)||(resourcesOnly&&!resourceDefinitions.Add(definition)))continue;
                var members=definitions[definition]!;
                ancestors.Add(definition);
                frames.Push(new(members.GetEnumerator(),world,path,root,definition));
            }
        }
        finally{while(frames.TryPop(out var frame))frame.Items.Dispose();}
        result.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Path,b.Path));
        return new(result,cyclic.Count,missing);
    }
    // Corrupt cyclic components are isolated before expansion; a GS inside a cycle cannot make
    // exponentially many otherwise GS-free paths productive. Ordinary acyclic siblings remain valid.
    private static HashSet<Guid> FindCyclicDefinitions(Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>?> definitions,
        Dictionary<Guid,HashSet<Guid>> parents,CancellationToken ct)
    {
        IEnumerable<Guid> Edges(Guid id)=>definitions[id]?.Where(n=>!n.IsGaussian&&n.Definition.HasValue).Select(n=>n.Definition!.Value)??Enumerable.Empty<Guid>();
        var visited=new HashSet<Guid>();var finished=new List<Guid>();var pending=new Stack<(Guid Id,bool Exit)>();
        foreach(var start in definitions.Keys)
        {
            if(visited.Contains(start))continue;pending.Push((start,false));
            while(pending.TryPop(out var frame))
            {
                ct.ThrowIfCancellationRequested();
                if(frame.Exit){finished.Add(frame.Id);continue;}
                if(!visited.Add(frame.Id))continue;
                pending.Push((frame.Id,true));foreach(var child in Edges(frame.Id))if(!visited.Contains(child))pending.Push((child,false));
            }
        }
        visited.Clear();var cyclic=new HashSet<Guid>();var component=new List<Guid>();var reverse=new Stack<Guid>();
        for(int i=finished.Count-1;i>=0;i--)
        {
            Guid start=finished[i];if(!visited.Add(start))continue;component.Clear();reverse.Push(start);
            while(reverse.TryPop(out var id))
            {
                ct.ThrowIfCancellationRequested();component.Add(id);
                if(parents.TryGetValue(id,out var previous))foreach(var parent in previous)if(visited.Add(parent))reverse.Push(parent);
            }
            if(component.Count>1||Edges(start).Contains(start))cyclic.UnionWith(component);
        }
        return cyclic;
    }
    // Whole-root suppression is safe only for a top-level GS reference, never a mixed ordinary container.
    public static bool CanSuppressWholeRoot(GaussianInstanceOccurrence occurrence,Guid drawnObject,Guid? activeTopLevel)
        =>occurrence.IsTopLevel&&occurrence.World is not null&&occurrence.ObjectId==occurrence.RootId&&
            (drawnObject==occurrence.RootId||activeTopLevel==occurrence.RootId);
    public static GaussianTransform? Compose(GaussianTransform? parent,GaussianTransform? local)
    {
        if(parent is not GaussianTransform p||local is not GaussianTransform c)return null;
        var result=new GaussianTransform(p.Linear*c.Linear,p.Linear.Transform(c.Translation)+p.Translation);
        if(!result.Linear.IsFinite||!result.Translation.IsFinite)return null;
        return result;
    }
    private static GaussianTransform? Renderable(GaussianTransform? transform)
    {
        if(transform is not GaussianTransform value)return null;
        try{_=value.Linear.Inverse();}catch(ArgumentException){return null;}
        return value;
    }
}
