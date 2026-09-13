using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SpectersOfSkyrimRandomizer.Tests;

using PlacedNpcContext = IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter>;

[TestClass]
public sealed class EncounterMutatorTests
{
    private static readonly ModKey WinningModKey = ModKey.FromFileName("Winning.esp");
    private static readonly ModKey PatchModKey = ModKey.FromFileName("Patch.esp");

    [TestMethod]
    public void RejectedPairGainsInitiallyDisabledAndExpectedOverrideCount()
    {
        var fixture = CreateFixture();
        var patch = NewPatch();

        var result = EncounterMutator.Apply(
            patch,
            [new PlannedMutation(false, fixture.SpecterContext, fixture.SkeletonContext)]);

        var overrides = patch.EnumerateMajorRecords<IPlacedNpcGetter>().ToArray();
        Assert.AreEqual(1, result.SpecterOverridesWritten);
        Assert.AreEqual(1, result.SkeletonOverridesWritten);
        Assert.AreEqual(2, result.TotalOverridesWritten);
        Assert.AreEqual(2, overrides.Length);
        Assert.IsTrue(overrides.All(record =>
            record.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled)));
    }

    [TestMethod]
    public void WinningDownstreamFieldsMajorFlagsAndVmadAreForwarded()
    {
        var fixture = CreateFixture();
        fixture.Specter.MajorFlags = PlacedNpc.MajorFlag.Persistent;
        fixture.Specter.Count = 7;
        fixture.Specter.Placement = new Placement
        {
            Position = new P3Float(101.5f, -22.25f, 33.75f),
            Rotation = new P3Float(0.1f, 0.2f, 0.3f)
        };
        fixture.Specter.VirtualMachineAdapter = NightVmad();
        var patch = NewPatch();

        EncounterMutator.Apply(
            patch,
            [new PlannedMutation(false, fixture.SpecterContext, fixture.SkeletonContext)]);

        var forwarded = patch.EnumerateMajorRecords<IPlacedNpcGetter>()
            .Single(record => record.FormKey == fixture.Specter.FormKey);
        Assert.IsTrue(forwarded.MajorFlags.HasFlag(PlacedNpc.MajorFlag.Persistent));
        Assert.IsTrue(forwarded.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled));
        Assert.AreEqual(7, forwarded.Count);
        Assert.AreEqual(fixture.Specter.Placement.Position, forwarded.Placement!.Position);
        Assert.AreEqual(fixture.Specter.Placement.Rotation, forwarded.Placement.Rotation);

        var script = forwarded.VirtualMachineAdapter!.Scripts.Single();
        Assert.AreEqual("AutomaticLightSwitchScript", script.Name);
        Assert.AreEqual(22.0f, script.Properties.OfType<IScriptFloatPropertyGetter>()
            .Single(property => property.Name == "LightsOnTime").Data);
        Assert.AreEqual(5.0f, script.Properties.OfType<IScriptFloatPropertyGetter>()
            .Single(property => property.Name == "LightsOffTime").Data);
    }

    [TestMethod]
    public void SelectedPairProducesNoOverrides()
    {
        var fixture = CreateFixture();
        var patch = NewPatch();

        var result = EncounterMutator.Apply(
            patch,
            [new PlannedMutation(true, fixture.SpecterContext, fixture.SkeletonContext)]);

        Assert.AreEqual(0, result.TotalOverridesWritten);
        Assert.AreEqual(0, patch.EnumerateMajorRecords<IPlacedNpcGetter>().Count());
    }

    [TestMethod]
    public void AlreadyDisabledRecordsAreSkippedAndNeverReenabled()
    {
        var fixture = CreateFixture();
        fixture.Specter.MajorFlags |= PlacedNpc.MajorFlag.InitiallyDisabled;
        var patch = NewPatch();

        var result = EncounterMutator.Apply(
            patch,
            [new PlannedMutation(false, fixture.SpecterContext, fixture.SkeletonContext)]);

        Assert.AreEqual(0, result.SpecterOverridesWritten);
        Assert.AreEqual(1, result.SkeletonOverridesWritten);
        Assert.AreEqual(1, result.TotalAlreadyDisabledSkipped);
        Assert.IsTrue(fixture.Specter.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled));
        Assert.IsFalse(patch.EnumerateMajorRecords<IPlacedNpcGetter>()
            .Any(record => record.FormKey == fixture.Specter.FormKey));
    }

    [TestMethod]
    public void DeletedWinningRecordFailsBeforeAnyOverrideIsWritten()
    {
        var fixture = CreateFixture();
        fixture.Specter.IsDeleted = true;
        var patch = NewPatch();

        Assert.ThrowsExactly<InvalidOperationException>(() => EncounterMutator.Apply(
            patch,
            [new PlannedMutation(false, fixture.SpecterContext, fixture.SkeletonContext)]));
        Assert.AreEqual(0, patch.EnumerateMajorRecords<IPlacedNpcGetter>().Count());
    }

    [TestMethod]
    public void UnrelatedPlacedNpcIsNeverTouched()
    {
        var fixture = CreateFixture(includeUnrelated: true);
        var patch = NewPatch();

        EncounterMutator.Apply(
            patch,
            [new PlannedMutation(false, fixture.SpecterContext, fixture.SkeletonContext)]);

        CollectionAssert.AreEquivalent(
            new[] { fixture.Specter.FormKey, fixture.Skeleton.FormKey },
            patch.EnumerateMajorRecords<IPlacedNpcGetter>().Select(record => record.FormKey).ToArray());
        Assert.IsFalse(fixture.Unrelated!.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled));
    }

    [TestMethod]
    public void SameSettingsProduceIdenticalMutationTargets()
    {
        var first = CreateFixture();
        var second = CreateFixture();
        var selected = DeterministicSelector.IsSelected(first.Specter.FormKey, 38174, 5.0);
        var firstPatch = NewPatch();
        var secondPatch = NewPatch();

        EncounterMutator.Apply(
            firstPatch,
            [new PlannedMutation(selected, first.SpecterContext, first.SkeletonContext)]);
        EncounterMutator.Apply(
            secondPatch,
            [new PlannedMutation(selected, second.SpecterContext, second.SkeletonContext)]);

        CollectionAssert.AreEquivalent(
            firstPatch.EnumerateMajorRecords<IPlacedNpcGetter>().Select(record => record.FormKey).ToArray(),
            secondPatch.EnumerateMajorRecords<IPlacedNpcGetter>().Select(record => record.FormKey).ToArray());
    }

    private static Fixture CreateFixture(bool includeUnrelated = false)
    {
        var mod = new SkyrimMod(WinningModKey, SkyrimRelease.SkyrimSE);
        var block = new CellBlock();
        var subBlock = new CellSubBlock();
        var cell = new Cell(mod);
        var specter = NewPlacedNpc(mod, 0x100, "WinningSpecter");
        var skeleton = NewPlacedNpc(mod, 0x200, "WinningSkeleton");
        cell.Temporary.Add(specter);
        cell.Temporary.Add(skeleton);

        PlacedNpc? unrelated = null;
        if (includeUnrelated)
        {
            unrelated = NewPlacedNpc(mod, 0x300, "Unrelated");
            cell.Temporary.Add(unrelated);
        }

        subBlock.Cells.Add(cell);
        block.SubBlocks.Add(subBlock);
        mod.Cells.Add(block);

        var cache = mod.ToImmutableLinkCache();
        var contexts = mod
            .EnumerateMajorRecordContexts<IPlacedNpc, IPlacedNpcGetter>(cache)
            .ToDictionary(context => context.Record.FormKey);
        return new Fixture(
            mod,
            specter,
            skeleton,
            unrelated,
            contexts[specter.FormKey],
            contexts[skeleton.FormKey]);
    }

    private static PlacedNpc NewPlacedNpc(SkyrimMod mod, uint id, string editorId)
    {
        var placedNpc = new PlacedNpc(new FormKey(mod.ModKey, id), SkyrimRelease.SkyrimSE)
        {
            EditorID = editorId,
            Placement = new Placement { Position = new P3Float(id, 2, 3) }
        };
        placedNpc.Base.SetTo(new FormKey(DryRunPatcher.SourceModKey, 0x900));
        return placedNpc;
    }

    private static VirtualMachineAdapter NightVmad()
    {
        return new VirtualMachineAdapter
        {
            Scripts =
            {
                new ScriptEntry
                {
                    Name = "AutomaticLightSwitchScript",
                    Properties =
                    {
                        new ScriptFloatProperty { Name = "LightsOnTime", Data = 22.0f },
                        new ScriptFloatProperty { Name = "LightsOffTime", Data = 5.0f }
                    }
                }
            }
        };
    }

    private static SkyrimMod NewPatch()
    {
        return new SkyrimMod(PatchModKey, SkyrimRelease.SkyrimSE);
    }

    private sealed record Fixture(
        SkyrimMod Mod,
        PlacedNpc Specter,
        PlacedNpc Skeleton,
        PlacedNpc? Unrelated,
        PlacedNpcContext SpecterContext,
        PlacedNpcContext SkeletonContext);
}
