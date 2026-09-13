using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Synthesis.CLI;
using Noggog;

namespace SpectersOfSkyrimRandomizer.Tests;

#pragma warning disable CS0618 // SynthesisState supplies a complete in-memory IPatcherState test fixture.

[TestClass]
public sealed class PatcherIntegrationTests
{
    [TestMethod]
    public void DryRunProducesZeroFunctionalRecords()
    {
        using var state = CreateState();

        DryRunPatcher.Run(
            state,
            new Settings { Probability = 5.0, Seed = 38174, DryRun = true },
            new StringWriter());

        Assert.AreEqual(0, state.PatchMod.EnumerateMajorRecords().Count());
    }

    [TestMethod]
    public void ApplyModeProducesDeterministicPairedOverrides()
    {
        using var first = CreateState();
        using var second = CreateState();
        var settings = new Settings { Probability = 5.0, Seed = 38174, DryRun = false };

        DryRunPatcher.Run(first, settings, new StringWriter());
        DryRunPatcher.Run(second, settings, new StringWriter());

        var firstOverrides = first.PatchMod.EnumerateMajorRecords<IPlacedNpcGetter>()
            .Select(record => record.FormKey)
            .OrderBy(key => key.ID)
            .ToArray();
        var secondOverrides = second.PatchMod.EnumerateMajorRecords<IPlacedNpcGetter>()
            .Select(record => record.FormKey)
            .OrderBy(key => key.ID)
            .ToArray();
        CollectionAssert.AreEqual(firstOverrides, secondOverrides);
        var selectedCount = Enumerable.Range(0, 93).Count(index =>
            DeterministicSelector.IsSelected(
                new FormKey(DryRunPatcher.SourceModKey, (uint)(0x1000 + index)),
                settings.Seed,
                settings.Probability));
        Assert.AreEqual((93 - selectedCount) * 2, firstOverrides.Length);
    }

    private static SynthesisState<ISkyrimMod, ISkyrimModGetter> CreateState()
    {
        var source = CreateSourceMod();
        var listing = new ModListing<ISkyrimModGetter>(source, enabled: true, ghostSuffix: string.Empty);
        var loadOrder = new LoadOrder<IModListing<ISkyrimModGetter>>([listing], disposeItems: false);
        var linkCache = loadOrder.ToImmutableLinkCache<ISkyrimMod, ISkyrimModGetter>();
        var patch = new SkyrimMod(ModKey.FromFileName("Patch.esp"), SkyrimRelease.SkyrimSE);
        return new SynthesisState<ISkyrimMod, ISkyrimModGetter>(
            new RunSynthesisMutagenPatcher(),
            [],
            loadOrder,
            linkCache,
            null!,
            patch,
            null,
            null,
            null,
            CancellationToken.None,
            null!);
    }

    private static SkyrimMod CreateSourceMod()
    {
        var mod = new SkyrimMod(DryRunPatcher.SourceModKey, SkyrimRelease.SkyrimSE);
        var specterBase = new Npc(mod, "LvlSpecterOfSkyrim");
        var skeletonBase = new Npc(mod, "TreasSpecterOfSkyrimSkeletonRigid");
        mod.Npcs.Add(specterBase);
        mod.Npcs.Add(skeletonBase);

        var block = new CellBlock();
        var subBlock = new CellSubBlock();
        var cell = new Cell(mod);

        for (var index = 0; index < 93; index++)
        {
            var position = new P3Float(index * 10.0f, index * -2.0f, index + 0.5f);
            var specter = new PlacedNpc(
                new FormKey(mod.ModKey, (uint)(0x1000 + index)),
                SkyrimRelease.SkyrimSE)
            {
                Placement = new Placement { Position = position },
                VirtualMachineAdapter = NightVmad()
            };
            specter.Base.SetTo(specterBase.FormKey);

            var skeleton = new PlacedNpc(
                new FormKey(mod.ModKey, (uint)(0x2000 + index)),
                SkyrimRelease.SkyrimSE)
            {
                Placement = new Placement { Position = position }
            };
            skeleton.Base.SetTo(skeletonBase.FormKey);

            cell.Temporary.Add(specter);
            cell.Temporary.Add(skeleton);
        }

        subBlock.Cells.Add(cell);
        block.SubBlocks.Add(subBlock);
        mod.Cells.Add(block);
        return mod;
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

}

#pragma warning restore CS0618
