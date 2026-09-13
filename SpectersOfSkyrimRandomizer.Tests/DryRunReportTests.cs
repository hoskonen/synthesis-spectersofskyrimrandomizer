using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mutagen.Bethesda.Plugins;

namespace SpectersOfSkyrimRandomizer.Tests;

[TestClass]
public sealed class DryRunReportTests
{
    private static readonly ModKey SourceMod = ModKey.FromFileName("SpectersOfSkyrim.esp");

    [TestMethod]
    public void SummaryContainsManualValidationFieldsAndNoMutationConfirmation()
    {
        var output = new StringWriter();
        var pairing = new PairingReport([], 1, 2, 3);
        var plans = new[]
        {
            Plan(0x200, selected: true, WinningRecordState.Available),
            Plan(0x100, selected: false, WinningRecordState.Missing),
            Plan(0x300, selected: false, WinningRecordState.Deleted)
        };

        DryRunPatcher.WriteReport(
            output,
            new Settings { Probability = 5.0, Seed = 38174, DryRun = true },
            pairing,
            plans);

        var report = output.ToString();
        StringAssert.Contains(report, "Source plugin: SpectersOfSkyrim.esp");
        StringAssert.Contains(report, "Algorithm: v1");
        StringAssert.Contains(report, "Probability: 5%");
        StringAssert.Contains(report, "Seed: 38174");
        StringAssert.Contains(report, "Source candidates: 3");
        StringAssert.Contains(report, "Valid encounter pairs: 0");
        StringAssert.Contains(report, "Selected encounters: 1");
        StringAssert.Contains(report, "Rejected encounters: 2");
        StringAssert.Contains(report, "Missing/deleted winning records: 2");
        StringAssert.Contains(report, "Pairing errors: 6");
        StringAssert.Contains(report, "No records were modified.");
    }

    [TestMethod]
    public void SelectedSourceFormKeysArePrintedInStableLocalFormIdOrder()
    {
        var output = new StringWriter();
        var plans = new[]
        {
            Plan(0x300, selected: true, WinningRecordState.Available),
            Plan(0x100, selected: true, WinningRecordState.Available),
            Plan(0x200, selected: true, WinningRecordState.Available)
        };

        DryRunPatcher.WriteReport(
            output,
            new Settings(),
            new PairingReport([], 0, 0, 0),
            plans);

        var report = output.ToString();
        var first = report.IndexOf("000100:SpectersOfSkyrim.esp", StringComparison.Ordinal);
        var second = report.IndexOf("000200:SpectersOfSkyrim.esp", StringComparison.Ordinal);
        var third = report.IndexOf("000300:SpectersOfSkyrim.esp", StringComparison.Ordinal);

        Assert.IsTrue(first >= 0 && first < second && second < third);
    }

    private static PlannedEncounter Plan(uint id, bool selected, WinningRecordState state)
    {
        return new PlannedEncounter(new FormKey(SourceMod, id), selected, id, state);
    }
}
