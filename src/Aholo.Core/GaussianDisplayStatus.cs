namespace Aholo.Core;

/// <summary>Document resources and instances are separate counts: one asset may back many instances.</summary>
public sealed record GaussianDisplayFacts
{
    public int DocumentInstances { get; init; }
    public int LoadedResources { get; init; }
    public int LoadingResources { get; init; }
    public int ResourceErrors { get; init; }
    public int VisibleInstances { get; init; }
    public int ReadyPlacements { get; init; }
    public bool IsModelView { get; init; } = true;
    public bool IsGsMode { get; init; } = true;
    public bool HasCandidate { get; init; }
    public bool HasFrame { get; init; }
    public bool FrameComplete { get; init; }
    public bool UploadPending { get; init; }
    public bool FrameFailed { get; init; }
    public int InputCount { get; init; }
    public int ProjectedCount { get; init; }
}

public enum GaussianDisplayPhase
{
    NoRecognizedInstances,
    WaitingForModelView,
    NoVisibleInstances,
    WaitingForResources,
    LoadingResources,
    ResourceLoadFailed,
    NoRenderablePlacements,
    WaitingForGsMode,
    WaitingForStableView,
    WaitingForFrame,
    Preparing,
    Uploading,
    PreparationFailed,
    NoProjectedGaussians,
    PartiallyReady,
    Ready
}

/// <summary>Describes what can actually be displayed; an empty completed frame is never success.</summary>
public static class GaussianDisplayStatus
{
    /// <summary>A usable partial frame does not hide pending or failed visible resources.</summary>
    public static bool ShouldShowOverlay(GaussianDisplayPhase phase, bool frameAccepted) =>
        phase != GaussianDisplayPhase.Ready || !frameAccepted;

    public static GaussianDisplayPhase Evaluate(GaussianDisplayFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        // Document/view facts outrank a stale frame left over from a previously visible scene.
        if (facts.DocumentInstances <= 0) return GaussianDisplayPhase.NoRecognizedInstances;
        if (!facts.IsModelView) return GaussianDisplayPhase.WaitingForModelView;
        if (facts.VisibleInstances <= 0) return GaussianDisplayPhase.NoVisibleInstances;
        if (facts.ReadyPlacements <= 0)
        {
            if (facts.LoadingResources > 0) return GaussianDisplayPhase.LoadingResources;
            if (facts.ResourceErrors > 0) return GaussianDisplayPhase.ResourceLoadFailed;
            return facts.LoadedResources > 0 ? GaussianDisplayPhase.NoRenderablePlacements : GaussianDisplayPhase.WaitingForResources;
        }
        if (!facts.IsGsMode) return GaussianDisplayPhase.WaitingForGsMode;
        if (facts.FrameFailed) return GaussianDisplayPhase.PreparationFailed;
        if (facts.HasCandidate) return GaussianDisplayPhase.WaitingForStableView;
        if (!facts.HasFrame || facts.InputCount <= 0) return GaussianDisplayPhase.WaitingForFrame;
        if (facts.FrameComplete)
        {
            if (facts.ProjectedCount <= 0) return GaussianDisplayPhase.NoProjectedGaussians;
            return facts.ReadyPlacements < facts.VisibleInstances ? GaussianDisplayPhase.PartiallyReady : GaussianDisplayPhase.Ready;
        }
        return facts.UploadPending ? GaussianDisplayPhase.Uploading : GaussianDisplayPhase.Preparing;
    }
}
