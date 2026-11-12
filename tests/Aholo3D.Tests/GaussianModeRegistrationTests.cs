using Aholo3D.Rhino;

namespace Aholo3D.Tests;

public sealed class GaussianModeRegistrationTests
{
    private const string Name = "Aholo3D GS (Experimental)";
    private static GaussianDisplayModeSnapshot Ready(Guid? id = null, string name = Name) =>
        new(id ?? Guid.NewGuid(), name, true, false, true, true, false, true, false, true, false);

    [Fact]
    public void RepeatedStartupReusesCorrectModeWithoutUpdating()
    {
        GaussianDisplayModeSnapshot existing = Ready(); var store = new FakeStore(existing);
        Assert.Equal(existing.Id, GaussianModeRegistration.Ensure(store, existing.Id, Name));
        Assert.Equal(existing.Id, GaussianModeRegistration.Ensure(store, store.SavedId, Name));
        Assert.Equal(0, store.CopyCalls); Assert.Empty(store.Updates); Assert.Equal(2, store.SaveCalls);
    }

    [Fact]
    public void InvalidSavedIdReusesExactNameAndPersistsRecoveredId()
    {
        GaussianDisplayModeSnapshot existing = Ready(); var store = new FakeStore(existing);
        Assert.Equal(existing.Id, GaussianModeRegistration.Ensure(store, Guid.NewGuid(), Name));
        Assert.Equal(existing.Id, store.SavedId); Assert.Equal(0, store.CopyCalls); Assert.Empty(store.Updates);
    }

    [Fact]
    public void SavedIdTakesPriorityAmongSameNameModes()
    {
        GaussianDisplayModeSnapshot first = Ready(), saved = Ready(); var store = new FakeStore(first, saved);
        Assert.Equal(saved.Id, GaussianModeRegistration.Ensure(store, saved.Id, Name));
        Assert.Empty(store.Updates); Assert.Equal(0, store.CopyCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateBooleanCannotOverrideSuccessfulReadback(bool reported)
    {
        GaussianDisplayModeSnapshot existing = Ready() with { ShadeVertexColors = false };
        var store = new FakeStore(existing) { UpdateReported = reported };
        Assert.Equal(existing.Id, GaussianModeRegistration.Ensure(store, existing.Id, Name));
        Assert.Single(store.Updates); Assert.Equal(2, store.GetCalls); Assert.Equal(Ready(existing.Id), store.Modes[existing.Id]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateBooleanCannotOverrideIncorrectReadback(bool reported)
    {
        GaussianDisplayModeSnapshot existing = Ready() with { ShadeVertexColors = false };
        var store = new FakeStore(existing) { UpdateReported = reported, ApplyUpdate = false };
        Assert.Throws<InvalidOperationException>(() => GaussianModeRegistration.Ensure(store, existing.Id, Name));
        Assert.Single(store.Updates); Assert.Equal(2, store.GetCalls); Assert.Equal(existing.Id, store.SavedId);
    }

    [Fact]
    public void MissingReadbackCannotClaimSuccess()
    {
        GaussianDisplayModeSnapshot existing = Ready() with { InMenu = false };
        var store = new FakeStore(existing) { RemoveOnUpdate = true };
        Assert.Throws<InvalidOperationException>(() => GaussianModeRegistration.Ensure(store, existing.Id, Name));
        Assert.Single(store.Updates);
    }

    [Fact]
    public void CreationFailureDoesNotPersistAnEmptyId()
    {
        var store = new FakeStore { CopyResult = Guid.Empty };
        Assert.Throws<InvalidOperationException>(() => GaussianModeRegistration.Ensure(store, Guid.Empty, Name));
        Assert.Equal(1, store.CopyCalls); Assert.Equal(0, store.SaveCalls); Assert.Empty(store.Updates);
    }

    [Fact]
    public void NewlyCopiedIdIsSavedBeforeReadback()
    {
        var store = new FakeStore { HideUntilSaved = true };
        Guid result = GaussianModeRegistration.Ensure(store, Guid.Empty, Name);
        Assert.Equal(store.CopyResult, result); Assert.Equal(result, store.SavedId);
        Assert.Equal(1, store.CopyCalls); Assert.Single(store.Updates);
        Assert.Equal(Ready(result), store.Modes[result]);
    }

    [Fact]
    public void FailedUpdateRetryReusesSavedCreationInsteadOfCopyingAgain()
    {
        var store = new FakeStore { UpdateReported = false, ApplyUpdate = false };
        Assert.Throws<InvalidOperationException>(() => GaussianModeRegistration.Ensure(store, Guid.Empty, Name));
        Guid createdId = store.SavedId;
        Assert.NotEqual(Guid.Empty, createdId); Assert.Equal(1, store.CopyCalls);
        store.ApplyUpdate = true;
        Assert.Equal(createdId, GaussianModeRegistration.Ensure(store, store.SavedId, Name));
        Assert.Equal(1, store.CopyCalls); Assert.Equal(2, store.Updates.Count);
    }

    [Fact]
    public void FailedCreationReadbackRetryReusesPersistedMode()
    {
        var store = new FakeStore { MissingGets = 1 };
        Assert.Throws<InvalidOperationException>(() => GaussianModeRegistration.Ensure(store, Guid.Empty, Name));
        Assert.NotEqual(Guid.Empty, store.SavedId);
        Assert.Equal(store.SavedId, GaussianModeRegistration.Ensure(store, store.SavedId, Name));
        Assert.Equal(1, store.CopyCalls);
    }

    [Fact]
    public void UnrelatedSavedModeAndDifferentlyCasedNameAreNeverChanged()
    {
        GaussianDisplayModeSnapshot unrelated = Ready(name: "Shaded"), differentCase = Ready(name: "aholo3d GS (Experimental)");
        var store = new FakeStore(unrelated, differentCase);
        Guid result = GaussianModeRegistration.Ensure(store, unrelated.Id, Name);
        Assert.Equal(store.CopyResult, result); Assert.Equal(1, store.CopyCalls);
        Assert.Equal(unrelated, store.Modes[unrelated.Id]); Assert.Equal(differentCase, store.Modes[differentCase.Id]);
        Assert.All(store.Updates, update => { Assert.Equal(result, update.Id); Assert.Equal(Name, update.EnglishName); });
    }

    [Fact]
    public void UnrelatedSavedModeFallsBackToExistingExactName()
    {
        GaussianDisplayModeSnapshot unrelated = Ready(name: "Shaded"), target = Ready() with { InMenu = false };
        var store = new FakeStore(unrelated, target);
        Assert.Equal(target.Id, GaussianModeRegistration.Ensure(store, unrelated.Id, Name));
        Assert.Equal(0, store.CopyCalls); Assert.Equal(unrelated, store.Modes[unrelated.Id]);
        Assert.Equal(target.Id, Assert.Single(store.Updates).Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EachRequiredSettingIsRepairedAndVerified(int setting)
    {
        GaussianDisplayModeSnapshot ready = Ready();
        GaussianDisplayModeSnapshot changed = setting switch
        {
            0 => ready with { InMenu = false }, 1 => ready with { AllowObjectAssignment = true },
            2 => ready with { SupportsShading = false }, 3 => ready with { ShadedPipelineRequired = false },
            4 => ready with { WireframePipelineRequired = true }, 5 => ready with { ShadingEnabled = false },
            6 => ready with { DisableTransparency = true }, 7 => ready with { ShadeVertexColors = false },
            8 => ready with { FrontFlatShaded = true }, _ => throw new ArgumentOutOfRangeException(nameof(setting))
        };
        var store = new FakeStore(changed);
        Assert.Equal(ready.Id, GaussianModeRegistration.Ensure(store, ready.Id, Name));
        Assert.Equal(ready, Assert.Single(store.Updates)); Assert.Equal(ready, store.Modes[ready.Id]);
    }

    [Fact]
    public void SavedLegacyModeRenamesInPlaceWithoutChangingDocumentModeIdentity()
    {
        var old=Ready(name:"Aholo GS (Experimental)");var store=new FakeStore(old);
        Assert.Equal(old.Id,GaussianModeRegistration.Ensure(store,old.Id,Name,"Aholo GS (Experimental)"));
        Assert.Equal(Name,store.Modes[old.Id].EnglishName);Assert.Equal(0,store.CopyCalls);
    }
    [Fact]
    public void FailedRenameKeepsLegacyModeAndDoesNotCreateAnother()
    {
        var old=Ready(name:"Aholo GS (Experimental)");var store=new FakeStore(old){ApplyUpdate=false};
        Assert.Throws<InvalidOperationException>(()=>GaussianModeRegistration.Ensure(store,old.Id,Name,"Aholo GS (Experimental)"));
        Assert.Equal(old,store.Modes[old.Id]);Assert.Equal(0,store.CopyCalls);
    }
    [Fact]
    public void UnownedLegacyModeIsNeverRenamed()
    {
        var old=Ready(name:"Aholo GS (Experimental)");var store=new FakeStore(old);
        Assert.NotEqual(old.Id,GaussianModeRegistration.Ensure(store,Guid.Empty,Name,"Aholo GS (Experimental)"));
        Assert.Equal(old,store.Modes[old.Id]);
    }

    private sealed class FakeStore : IGaussianModeStore
    {
        internal readonly Dictionary<Guid, GaussianDisplayModeSnapshot> Modes = new();
        internal readonly List<GaussianDisplayModeSnapshot> Updates = new();
        internal Guid SavedId, CopyResult = Guid.NewGuid();
        internal int CopyCalls, SaveCalls, GetCalls, MissingGets;
        internal bool UpdateReported = true, ApplyUpdate = true, RemoveOnUpdate, HideUntilSaved;
        internal FakeStore(params GaussianDisplayModeSnapshot[] modes)
        {
            foreach (GaussianDisplayModeSnapshot mode in modes) Modes.Add(mode.Id, mode);
        }
        public GaussianDisplayModeSnapshot? Get(Guid id)
        {
            GetCalls++;
            if (MissingGets > 0) { MissingGets--; return null; }
            if (HideUntilSaved && id != SavedId) return null;
            return Modes.GetValueOrDefault(id);
        }
        public IEnumerable<GaussianDisplayModeSnapshot> FindAll() => Modes.Values.ToArray();
        public Guid CopyShaded(string englishName)
        {
            CopyCalls++;
            if (CopyResult != Guid.Empty) Modes.Add(CopyResult, Ready(CopyResult, englishName) with { InMenu = false, ShadeVertexColors = false });
            return CopyResult;
        }
        public bool Update(GaussianDisplayModeSnapshot mode)
        {
            Updates.Add(mode);
            if (RemoveOnUpdate) Modes.Remove(mode.Id);
            else if (ApplyUpdate) Modes[mode.Id] = mode;
            return UpdateReported;
        }
        public bool Rename(Guid id,string expectedName,string newName)
        {
            if(!Modes.TryGetValue(id,out var old)||old.EnglishName!=expectedName)return false;
            if(ApplyUpdate)Modes[id]=old with {EnglishName=newName};
            return UpdateReported;
        }
        public void SaveId(Guid id) { SavedId = id; SaveCalls++; }
    }
}
