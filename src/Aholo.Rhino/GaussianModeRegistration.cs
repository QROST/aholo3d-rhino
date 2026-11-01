namespace Aholo.Rhino;

// Plain snapshots keep registration decisions testable without loading the Rhino runtime.
internal sealed record GaussianDisplayModeSnapshot(
    Guid Id,
    string EnglishName,
    bool InMenu,
    bool AllowObjectAssignment,
    bool SupportsShading,
    bool ShadedPipelineRequired,
    bool WireframePipelineRequired,
    bool ShadingEnabled,
    bool DisableTransparency,
    bool ShadeVertexColors,
    bool FrontFlatShaded);

internal interface IGaussianModeStore
{
    GaussianDisplayModeSnapshot? Get(Guid id);
    IEnumerable<GaussianDisplayModeSnapshot> FindAll();
    Guid CopyShaded(string englishName);
    bool Update(GaussianDisplayModeSnapshot mode);
    void SaveId(Guid id);
}

/// <summary>
/// Reuses a named mode and verifies the registered runtime settings. The host owns thread affinity, nonfatal errors and retry scheduling.
/// No mode is removed, and modes with a different exact name are never passed to Update.
/// </summary>
internal static class GaussianModeRegistration
{
    internal static Guid Ensure(IGaussianModeStore store, Guid savedId, string englishName)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(englishName)) throw new ArgumentException("显示模式名称不能为空。", nameof(englishName));

        GaussianDisplayModeSnapshot? mode = savedId == Guid.Empty ? null : store.Get(savedId);
        if (!Matches(mode, savedId, englishName)) mode = null;
        mode ??= store.FindAll().FirstOrDefault(candidate => candidate.Id != Guid.Empty &&
            string.Equals(candidate.EnglishName, englishName, StringComparison.Ordinal));

        if (mode is null)
        {
            Guid createdId = store.CopyShaded(englishName);
            if (createdId == Guid.Empty) throw new InvalidOperationException("无法注册 Aholo GS 实验显示模式。");
            // Persist immediately, before any readback/update can fail: retries must not keep copying Shaded.
            store.SaveId(createdId);
            mode = store.Get(createdId);
            if (!Matches(mode, createdId, englishName)) throw new InvalidOperationException("无法读取新建的 Aholo GS 显示模式。");
        }
        else store.SaveId(mode.Id);

        if (HasRequiredSettings(mode!)) return mode!.Id;
        GaussianDisplayModeSnapshot desired = mode! with
        {
            InMenu = true, AllowObjectAssignment = false, SupportsShading = true, ShadedPipelineRequired = true,
            WireframePipelineRequired = false, ShadingEnabled = true, DisableTransparency = false,
            ShadeVertexColors = true, FrontFlatShaded = false
        };
        // The native boolean alone cannot establish usable settings. Always verify the manager's runtime readback.
        // This does not prove disk persistence across a future Rhino restart.
        _ = store.Update(desired);
        GaussianDisplayModeSnapshot? persisted = store.Get(desired.Id);
        if (!Matches(persisted, desired.Id, englishName) || !HasRequiredSettings(persisted!))
            throw new InvalidOperationException("Aholo GS 显示模式设置尚未保存，请稍后重试。");
        return desired.Id;
    }

    private static bool Matches(GaussianDisplayModeSnapshot? mode, Guid id, string englishName) =>
        id != Guid.Empty && mode is not null && mode.Id == id && string.Equals(mode.EnglishName, englishName, StringComparison.Ordinal);

    private static bool HasRequiredSettings(GaussianDisplayModeSnapshot mode) =>
        mode.InMenu && !mode.AllowObjectAssignment && mode.SupportsShading && mode.ShadedPipelineRequired &&
        !mode.WireframePipelineRequired && mode.ShadingEnabled && !mode.DisableTransparency && mode.ShadeVertexColors && !mode.FrontFlatShaded;
}
