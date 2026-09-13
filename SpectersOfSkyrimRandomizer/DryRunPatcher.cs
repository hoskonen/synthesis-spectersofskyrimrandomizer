using System.Globalization;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

namespace SpectersOfSkyrimRandomizer;

public static class DryRunPatcher
{
    public static readonly ModKey SourceModKey = ModKey.FromFileName("SpectersOfSkyrim.esp");

    private const string SpecterBaseEditorId = "LvlSpecterOfSkyrim";
    private const string SkeletonBaseEditorId = "TreasSpecterOfSkyrimSkeletonRigid";
    private const string NightScriptName = "AutomaticLightSwitchScript";
    private const int ExpectedSpecterCount = 93;
    private const int ExpectedSkeletonCount = 93;

    public static void Run(
        IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
        Settings settings,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(output);

        DeterministicSelector.ValidateProbability(settings.Probability);
        if (!settings.DryRun)
        {
            throw new NotSupportedException(
                "DryRun=false is not supported in milestone 1. Skyrim record mutation has not been implemented.");
        }

        var initialPatchRecordCount = state.PatchMod.EnumerateMajorRecords().Count();
        var sourceListing = state.LoadOrder.ListedOrder
            .SingleOrDefault(listing => listing.ModKey == SourceModKey);
        var sourceMod = sourceListing?.Mod ?? throw new InvalidOperationException(
            $"Required source plugin '{SourceModKey.FileName}' is missing from the active load order.");

        var specterBase = ResolveUniqueSourceNpc(sourceMod, SpecterBaseEditorId);
        var skeletonBase = ResolveUniqueSourceNpc(sourceMod, SkeletonBaseEditorId);
        var sourcePlacements = ReadSourcePlacements(sourceMod);
        var specters = EncounterDiscovery.FindSourcePlacements(
            sourcePlacements,
            SourceModKey,
            specterBase.FormKey);
        var skeletons = EncounterDiscovery.FindSourcePlacements(
            sourcePlacements,
            SourceModKey,
            skeletonBase.FormKey);

        ValidateSourceSchema(specters, skeletons);
        ValidateNightScripts(sourceMod, specters.Select(x => x.FormKey));

        var pairing = EncounterDiscovery.ValidatePairs(specters, skeletons);
        if (pairing.ErrorCount != 0 || pairing.ValidPairs.Count != ExpectedSpecterCount)
        {
            throw new InvalidOperationException(
                "SpectersOfSkyrim.esp pairing schema is not the expected one-to-one source cell + exact XYZ relationship. " +
                $"Valid={pairing.ValidPairs.Count}, unmatched specters={pairing.UnmatchedSpecters}, " +
                $"unmatched skeletons={pairing.UnmatchedSkeletons}, ambiguous={pairing.AmbiguousPairings}.");
        }

        var candidateKeys = specters.Select(x => x.FormKey).ToHashSet();
        var winningRecords = state.LoadOrder.PriorityOrder
            .PlacedNpc()
            .WinningContextOverrides(state.LinkCache, includeDeletedRecords: true)
            .Where(context => candidateKeys.Contains(context.Record.FormKey))
            .ToDictionary(context => context.Record.FormKey, context => context.Record);

        var plans = specters.Select(specter =>
        {
            var winningState = GetWinningState(specter, winningRecords);
            return new PlannedEncounter(
                specter.FormKey,
                DeterministicSelector.IsSelected(
                    specter.FormKey,
                    settings.Seed,
                    settings.Probability),
                DeterministicSelector.ComputeScore(specter.FormKey, settings.Seed),
                winningState);
        }).ToArray();

        WriteReport(output, settings, pairing, plans);

        var finalPatchRecordCount = state.PatchMod.EnumerateMajorRecords().Count();
        if (finalPatchRecordCount != initialPatchRecordCount)
        {
            throw new InvalidOperationException(
                "Dry-run invariant violated: the patcher changed the output plugin record count.");
        }
    }

    private static INpcGetter ResolveUniqueSourceNpc(ISkyrimModGetter sourceMod, string editorId)
    {
        var matches = sourceMod.EnumerateMajorRecords<INpcGetter>()
            .Where(npc =>
                npc.FormKey.ModKey == SourceModKey &&
                string.Equals(npc.EditorID, editorId, StringComparison.Ordinal))
            .ToArray();

        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected exactly one source-defined NPC with Editor ID '{editorId}' in " +
                $"'{SourceModKey.FileName}', but found {matches.Length}.");
    }

    private static IReadOnlyList<SourcePlacement> ReadSourcePlacements(ISkyrimModGetter sourceMod)
    {
        var placements = new List<SourcePlacement>();

        foreach (var context in sourceMod.EnumerateMajorRecordSimpleContexts<IPlacedNpcGetter>())
        {
            if (context.Record.FormKey.ModKey != SourceModKey)
            {
                continue;
            }

            if (context.Record.IsDeleted)
            {
                throw new InvalidOperationException(
                    $"Source-defined ACHR {FormatFormKey(context.Record.FormKey)} is deleted in the source plugin.");
            }

            if (!context.TryGetParent<ICellGetter>(out var cell))
            {
                throw new InvalidOperationException(
                    $"Could not resolve the containing source cell for ACHR {FormatFormKey(context.Record.FormKey)}.");
            }

            if (context.Record.Placement is null)
            {
                throw new InvalidOperationException(
                    $"Source-defined ACHR {FormatFormKey(context.Record.FormKey)} has no placement position.");
            }

            if (context.Record.Base.IsNull)
            {
                throw new InvalidOperationException(
                    $"Source-defined ACHR {FormatFormKey(context.Record.FormKey)} has no base NPC.");
            }

            placements.Add(new SourcePlacement(
                context.Record.FormKey,
                context.Record.Base.FormKey,
                cell.FormKey,
                ExactPosition.From(context.Record.Placement.Position)));
        }

        return placements;
    }

    private static void ValidateSourceSchema(
        IReadOnlyCollection<SourcePlacement> specters,
        IReadOnlyCollection<SourcePlacement> skeletons)
    {
        if (specters.Count != ExpectedSpecterCount || skeletons.Count != ExpectedSkeletonCount)
        {
            throw new InvalidOperationException(
                $"Unexpected SpectersOfSkyrim.esp source schema. Expected {ExpectedSpecterCount} specter ACHRs " +
                $"and {ExpectedSkeletonCount} skeleton ACHRs, but found {specters.Count} and {skeletons.Count}.");
        }
    }

    private static void ValidateNightScripts(
        ISkyrimModGetter sourceMod,
        IEnumerable<FormKey> specterFormKeys)
    {
        var expectedKeys = specterFormKeys.ToHashSet();
        var invalid = new List<FormKey>();

        foreach (var specter in sourceMod.EnumerateMajorRecords<IPlacedNpcGetter>()
                     .Where(record => expectedKeys.Contains(record.FormKey)))
        {
            var scripts = specter.VirtualMachineAdapter?.Scripts
                .Where(script => string.Equals(script.Name, NightScriptName, StringComparison.Ordinal))
                .ToArray() ?? [];

            if (scripts.Length != 1 ||
                !HasFloatProperty(scripts[0], "LightsOnTime", 22.0f) ||
                !HasFloatProperty(scripts[0], "LightsOffTime", 5.0f))
            {
                invalid.Add(specter.FormKey);
            }
        }

        if (invalid.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected {NightScriptName} with LightsOnTime=22 and LightsOffTime=5 on every source specter. " +
                $"Invalid records: {string.Join(", ", invalid.Select(FormatFormKey))}.");
        }
    }

    private static bool HasFloatProperty(IScriptEntryGetter script, string propertyName, float value)
    {
        var matches = script.Properties
            .Where(property => string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            .OfType<IScriptFloatPropertyGetter>()
            .ToArray();
        return matches.Length == 1 && matches[0].Data.Equals(value);
    }

    private static WinningRecordState GetWinningState(
        SourcePlacement source,
        IReadOnlyDictionary<FormKey, IPlacedNpcGetter> winningRecords)
    {
        if (!winningRecords.TryGetValue(source.FormKey, out var winning))
        {
            return WinningRecordState.Missing;
        }

        if (winning.IsDeleted)
        {
            return WinningRecordState.Deleted;
        }

        return winning.Base.FormKey == source.BaseFormKey
            ? WinningRecordState.Available
            : WinningRecordState.UnexpectedBase;
    }

    private static void WriteReport(
        TextWriter output,
        Settings settings,
        PairingReport pairing,
        IReadOnlyCollection<PlannedEncounter> plans)
    {
        var selected = plans.Where(plan => plan.Selected).ToArray();
        var rejected = plans.Count - selected.Length;
        var missing = plans.Count(plan => plan.WinningState == WinningRecordState.Missing);
        var deleted = plans.Count(plan => plan.WinningState == WinningRecordState.Deleted);
        var unexpected = plans.Count(plan => plan.WinningState == WinningRecordState.UnexpectedBase);

        output.WriteLine("Specters of Skyrim Randomizer — Dry Run");
        output.WriteLine();
        output.WriteLine($"Source plugin: {SourceModKey.FileName}");
        output.WriteLine($"Algorithm: {DeterministicSelector.AlgorithmVersion}");
        output.WriteLine($"Probability: {settings.Probability.ToString("0.################", CultureInfo.InvariantCulture)}%");
        output.WriteLine($"Seed: {settings.Seed.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine();
        output.WriteLine($"Source specters: {plans.Count}");
        output.WriteLine($"Valid encounter pairs: {pairing.ValidPairs.Count}");
        output.WriteLine($"Selected encounters: {selected.Length}");
        output.WriteLine($"Rejected encounters: {rejected}");
        output.WriteLine($"Missing winning records: {missing}");
        output.WriteLine($"Deleted winning records: {deleted}");
        output.WriteLine($"Unexpected winning records: {unexpected}");
        output.WriteLine($"Pairing errors: {pairing.ErrorCount}");
        output.WriteLine();
        output.WriteLine("Selected source FormKeys:");
        foreach (var plan in selected.OrderBy(plan => plan.SourceFormKey.ID))
        {
            output.WriteLine(
                $"  {FormatFormKey(plan.SourceFormKey)} score=0x{plan.Score:X16} winning={plan.WinningState}");
        }

        if (selected.Length == 0)
        {
            output.WriteLine("  <none>");
        }

        output.WriteLine();
        output.WriteLine("No records were modified.");
    }

    private static string FormatFormKey(FormKey formKey)
    {
        return $"{formKey.ID:X6}:{formKey.ModKey.FileName.String}";
    }
}
