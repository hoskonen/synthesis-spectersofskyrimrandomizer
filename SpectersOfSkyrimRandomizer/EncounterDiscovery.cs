using Mutagen.Bethesda.Plugins;

namespace SpectersOfSkyrimRandomizer;

public static class EncounterDiscovery
{
    public static IReadOnlyList<SourcePlacement> FindSourcePlacements(
        IEnumerable<SourcePlacement> placements,
        ModKey sourceModKey,
        FormKey expectedBaseFormKey)
    {
        return placements
            .Where(placement =>
                placement.FormKey.ModKey == sourceModKey &&
                placement.BaseFormKey == expectedBaseFormKey)
            .OrderBy(placement => placement.FormKey.ID)
            .ToArray();
    }

    public static PairingReport ValidatePairs(
        IEnumerable<SourcePlacement> specters,
        IEnumerable<SourcePlacement> skeletons)
    {
        var spectersByKey = specters
            .GroupBy(ToPairingKey)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var skeletonsByKey = skeletons
            .GroupBy(ToPairingKey)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var allKeys = spectersByKey.Keys
            .Concat(skeletonsByKey.Keys)
            .Distinct()
            .OrderBy(key => key.CellFormKey.ModKey.FileName.String, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key.CellFormKey.ID)
            .ThenBy(key => key.Position.XBits)
            .ThenBy(key => key.Position.YBits)
            .ThenBy(key => key.Position.ZBits);

        var validPairs = new List<EncounterPair>();
        var unmatchedSpecters = 0;
        var unmatchedSkeletons = 0;
        var ambiguousPairings = 0;

        foreach (var key in allKeys)
        {
            spectersByKey.TryGetValue(key, out var matchingSpecters);
            skeletonsByKey.TryGetValue(key, out var matchingSkeletons);
            matchingSpecters ??= [];
            matchingSkeletons ??= [];

            if (matchingSpecters.Length == 1 && matchingSkeletons.Length == 1)
            {
                validPairs.Add(new EncounterPair(matchingSpecters[0], matchingSkeletons[0]));
            }
            else if (matchingSpecters.Length > 0 && matchingSkeletons.Length == 0)
            {
                unmatchedSpecters += matchingSpecters.Length;
            }
            else if (matchingSpecters.Length == 0 && matchingSkeletons.Length > 0)
            {
                unmatchedSkeletons += matchingSkeletons.Length;
            }
            else
            {
                ambiguousPairings++;
            }
        }

        return new PairingReport(
            validPairs,
            unmatchedSpecters,
            unmatchedSkeletons,
            ambiguousPairings);
    }

    private static PairingKey ToPairingKey(SourcePlacement placement)
    {
        return new PairingKey(placement.CellFormKey, placement.Position);
    }
}
