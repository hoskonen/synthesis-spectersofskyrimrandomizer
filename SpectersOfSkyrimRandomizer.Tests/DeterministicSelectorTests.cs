using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mutagen.Bethesda.Plugins;

namespace SpectersOfSkyrimRandomizer.Tests;

[TestClass]
public sealed class DeterministicSelectorTests
{
    private static readonly ModKey SourceMod = ModKey.FromFileName("SpectersOfSkyrim.esp");

    [TestMethod]
    public void ZeroPercentAlwaysRejects()
    {
        foreach (var formKey in Fixture())
        {
            Assert.IsFalse(DeterministicSelector.IsSelected(formKey, 38174, 0.0));
        }
    }

    [TestMethod]
    public void OneHundredPercentAlwaysSelects()
    {
        foreach (var formKey in Fixture())
        {
            Assert.IsTrue(DeterministicSelector.IsSelected(formKey, 38174, 100.0));
        }
    }

    [TestMethod]
    public void SameIdentityAndSeedProduceSameResult()
    {
        var formKey = new FormKey(SourceMod, 0x0008F5);
        var first = DeterministicSelector.IsSelected(formKey, 38174, 5.0);
        var second = DeterministicSelector.IsSelected(formKey, 38174, 5.0);
        Assert.AreEqual(first, second);
        Assert.AreEqual(
            DeterministicSelector.ComputeScore(formKey, 38174),
            DeterministicSelector.ComputeScore(formKey, 38174));
    }

    [TestMethod]
    public void EnumerationOrderDoesNotChangeResults()
    {
        var fixture = Fixture().ToArray();
        var forward = SelectByIdentity(fixture, 38174, 25.0);
        var reversed = SelectByIdentity(fixture.Reverse(), 38174, 25.0);
        CollectionAssert.AreEquivalent(forward, reversed);
    }

    [TestMethod]
    public void UnrelatedInsertionDoesNotChangeExistingResults()
    {
        var fixture = Fixture().ToArray();
        var original = fixture.ToDictionary(
            key => key,
            key => DeterministicSelector.IsSelected(key, 38174, 25.0));
        var expanded = fixture
            .Append(new FormKey(SourceMod, 0x00F000))
            .ToDictionary(
                key => key,
                key => DeterministicSelector.IsSelected(key, 38174, 25.0));

        foreach (var pair in original)
        {
            Assert.AreEqual(pair.Value, expanded[pair.Key]);
        }
    }

    [TestMethod]
    public void DifferentSeedsChangeAtLeastSomeFixtureResults()
    {
        var fixture = Fixture().ToArray();
        var seedOne = fixture.Select(key => DeterministicSelector.IsSelected(key, 38174, 50.0));
        var seedTwo = fixture.Select(key => DeterministicSelector.IsSelected(key, 12345, 50.0));
        Assert.IsTrue(seedOne.Zip(seedTwo).Any(pair => pair.First != pair.Second));
    }

    [TestMethod]
    public void InvalidProbabilitiesAreRejected()
    {
        var key = new FormKey(SourceMod, 0x0008F5);
        foreach (var probability in new[]
                 {
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity,
                     -0.001,
                     100.001
                 })
        {
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => DeterministicSelector.IsSelected(key, 0, probability));
        }
    }

    [TestMethod]
    public void GoldenVectorsFreezeV1Contract()
    {
        Assert.AreEqual(9_427_528_420_039_680_475UL, DeterministicSelector.ComputeScore(new FormKey(SourceMod, 0x0008F5), 38174));
        Assert.AreEqual(14_887_669_196_581_907_784UL, DeterministicSelector.ComputeScore(new FormKey(SourceMod, 0x0009C8), 38174));
        Assert.AreEqual(2_561_833_816_178_138_347UL, DeterministicSelector.ComputeScore(new FormKey(SourceMod, 0x0008F5), -1));
    }

    private static IEnumerable<FormKey> Fixture()
    {
        return Enumerable.Range(0, 93).Select(index => new FormKey(SourceMod, (uint)(0x800 + index * 7)));
    }

    private static FormKey[] SelectByIdentity(IEnumerable<FormKey> formKeys, int seed, double probability)
    {
        return formKeys
            .Where(key => DeterministicSelector.IsSelected(key, seed, probability))
            .OrderBy(key => key.ID)
            .ToArray();
    }
}
