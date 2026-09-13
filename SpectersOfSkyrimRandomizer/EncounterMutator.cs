using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace SpectersOfSkyrimRandomizer;

using PlacedNpcContext = IModContext<ISkyrimMod, ISkyrimModGetter, IPlacedNpc, IPlacedNpcGetter>;

internal sealed record PlannedMutation(
    bool Selected,
    PlacedNpcContext SpecterContext,
    PlacedNpcContext SkeletonContext);

internal sealed record MutationResult(
    int SpecterOverridesWritten,
    int SkeletonOverridesWritten,
    int SpecterAlreadyDisabledSkipped,
    int SkeletonAlreadyDisabledSkipped)
{
    public int TotalOverridesWritten => SpecterOverridesWritten + SkeletonOverridesWritten;

    public int TotalAlreadyDisabledSkipped =>
        SpecterAlreadyDisabledSkipped + SkeletonAlreadyDisabledSkipped;
}

internal static class EncounterMutator
{
    public static MutationResult Apply(
        ISkyrimMod patchMod,
        IReadOnlyCollection<PlannedMutation> plans)
    {
        ArgumentNullException.ThrowIfNull(patchMod);
        ArgumentNullException.ThrowIfNull(plans);

        ValidateRejectedRecords(plans);

        var specterOverrides = 0;
        var skeletonOverrides = 0;
        var specterAlreadyDisabled = 0;
        var skeletonAlreadyDisabled = 0;

        foreach (var plan in plans.Where(plan => !plan.Selected))
        {
            if (AddInitiallyDisabled(plan.SpecterContext, patchMod))
            {
                specterOverrides++;
            }
            else
            {
                specterAlreadyDisabled++;
            }

            if (AddInitiallyDisabled(plan.SkeletonContext, patchMod))
            {
                skeletonOverrides++;
            }
            else
            {
                skeletonAlreadyDisabled++;
            }
        }

        var result = new MutationResult(
            specterOverrides,
            skeletonOverrides,
            specterAlreadyDisabled,
            skeletonAlreadyDisabled);
        ValidateOutputInvariant(patchMod, plans, result);
        return result;
    }

    private static bool AddInitiallyDisabled(PlacedNpcContext context, ISkyrimMod patchMod)
    {
        if (context.Record.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled))
        {
            return false;
        }

        var placedNpcOverride = context.GetOrAddAsOverride(patchMod);
        placedNpcOverride.MajorFlags |= PlacedNpc.MajorFlag.InitiallyDisabled;
        return true;
    }

    private static void ValidateRejectedRecords(IEnumerable<PlannedMutation> plans)
    {
        var deleted = plans
            .Where(plan => !plan.Selected)
            .SelectMany(plan => new[] { plan.SpecterContext.Record, plan.SkeletonContext.Record })
            .Where(record => record.IsDeleted)
            .Select(record => record.FormKey)
            .ToArray();

        if (deleted.Length != 0)
        {
            throw new InvalidOperationException(
                $"Cannot mutate deleted winning ACHRs: {string.Join(", ", deleted)}.");
        }
    }

    private static void ValidateOutputInvariant(
        ISkyrimMod patchMod,
        IEnumerable<PlannedMutation> plans,
        MutationResult result)
    {
        var expectedFormKeys = plans
            .Where(plan => !plan.Selected)
            .SelectMany(plan => new[] { plan.SpecterContext.Record, plan.SkeletonContext.Record })
            .Where(record => !record.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled))
            .Select(record => record.FormKey)
            .ToHashSet();
        var actualRecords = patchMod
            .EnumerateMajorRecords<IPlacedNpcGetter>()
            .Where(record => expectedFormKeys.Contains(record.FormKey))
            .ToArray();
        var actualFormKeys = actualRecords.Select(record => record.FormKey).ToHashSet();

        if (result.TotalOverridesWritten != expectedFormKeys.Count ||
            !actualFormKeys.SetEquals(expectedFormKeys) ||
            actualRecords.Any(record =>
                !record.MajorFlags.HasFlag(PlacedNpc.MajorFlag.InitiallyDisabled)))
        {
            throw new InvalidOperationException(
                "Mutation invariant violated: generated ACHR overrides do not match rejected, " +
                "not-already-disabled encounter records.");
        }
    }
}
