using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mutagen.Bethesda.Plugins;

namespace SpectersOfSkyrimRandomizer.Tests;

[TestClass]
public sealed class EncounterDiscoveryTests
{
    private static readonly ModKey SourceMod = ModKey.FromFileName("SpectersOfSkyrim.esp");
    private static readonly ModKey OtherMod = ModKey.FromFileName("Other.esp");
    private static readonly FormKey SpecterBase = new(SourceMod, 0x9C4);
    private static readonly FormKey SkeletonBase = new(SourceMod, 0x9C5);
    private static readonly FormKey Cell = new(ModKey.FromFileName("Skyrim.esm"), 0x1234);

    [TestMethod]
    public void OnlySourceDefinedCandidatesQualify()
    {
        var placements = new[]
        {
            Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3),
            Placement(OtherMod, 0x101, SpecterBase, Cell, 1, 2, 3),
            Placement(SourceMod, 0x102, SkeletonBase, Cell, 1, 2, 3)
        };

        var candidates = EncounterDiscovery.FindSourcePlacements(placements, SourceMod, SpecterBase);

        Assert.AreEqual(1, candidates.Count);
        Assert.AreEqual(new FormKey(SourceMod, 0x100), candidates[0].FormKey);
    }

    [TestMethod]
    public void ExactCellAndPositionPairingSucceeds()
    {
        var specter = Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3);
        var skeleton = Placement(SourceMod, 0x900, SkeletonBase, Cell, 1, 2, 3);

        var report = EncounterDiscovery.ValidatePairs([specter], [skeleton]);

        Assert.AreEqual(1, report.ValidPairs.Count);
        Assert.AreEqual(0, report.ErrorCount);
    }

    [TestMethod]
    public void MissingSkeletonIsDetected()
    {
        var specter = Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3);
        var report = EncounterDiscovery.ValidatePairs([specter], []);

        Assert.AreEqual(1, report.UnmatchedSpecters);
        Assert.AreEqual(0, report.ValidPairs.Count);
    }

    [TestMethod]
    public void DuplicateSkeletonPairingIsAmbiguous()
    {
        var specter = Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3);
        var skeletonOne = Placement(SourceMod, 0x900, SkeletonBase, Cell, 1, 2, 3);
        var skeletonTwo = Placement(SourceMod, 0x901, SkeletonBase, Cell, 1, 2, 3);

        var report = EncounterDiscovery.ValidatePairs([specter], [skeletonOne, skeletonTwo]);

        Assert.AreEqual(1, report.AmbiguousPairings);
        Assert.AreEqual(0, report.ValidPairs.Count);
    }

    [TestMethod]
    public void FormIdProximityIsNotAPairingFallback()
    {
        var specter = Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3);
        var adjacentButElsewhere = Placement(SourceMod, 0x101, SkeletonBase, Cell, 1, 2, 4);

        var report = EncounterDiscovery.ValidatePairs([specter], [adjacentButElsewhere]);

        Assert.AreEqual(0, report.ValidPairs.Count);
        Assert.AreEqual(1, report.UnmatchedSpecters);
        Assert.AreEqual(1, report.UnmatchedSkeletons);
    }

    [TestMethod]
    public void SamePositionInDifferentCellDoesNotPair()
    {
        var otherCell = new FormKey(Cell.ModKey, Cell.ID + 1);
        var specter = Placement(SourceMod, 0x100, SpecterBase, Cell, 1, 2, 3);
        var skeleton = Placement(SourceMod, 0x900, SkeletonBase, otherCell, 1, 2, 3);

        var report = EncounterDiscovery.ValidatePairs([specter], [skeleton]);

        Assert.AreEqual(0, report.ValidPairs.Count);
        Assert.AreEqual(1, report.UnmatchedSpecters);
        Assert.AreEqual(1, report.UnmatchedSkeletons);
    }

    private static SourcePlacement Placement(
        ModKey origin,
        uint id,
        FormKey baseFormKey,
        FormKey cell,
        float x,
        float y,
        float z)
    {
        return new SourcePlacement(
            new FormKey(origin, id),
            baseFormKey,
            cell,
            ExactPosition.From(x, y, z));
    }
}
