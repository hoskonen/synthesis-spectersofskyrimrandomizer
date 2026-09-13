using Mutagen.Bethesda.Plugins;
using Noggog;

namespace SpectersOfSkyrimRandomizer;

public readonly record struct ExactPosition(int XBits, int YBits, int ZBits)
{
    public static ExactPosition From(P3Float position)
    {
        return new ExactPosition(
            BitConverter.SingleToInt32Bits(position.X),
            BitConverter.SingleToInt32Bits(position.Y),
            BitConverter.SingleToInt32Bits(position.Z));
    }

    public static ExactPosition From(float x, float y, float z)
    {
        return From(new P3Float(x, y, z));
    }
}

public sealed record SourcePlacement(
    FormKey FormKey,
    FormKey BaseFormKey,
    FormKey CellFormKey,
    ExactPosition Position);

public readonly record struct PairingKey(FormKey CellFormKey, ExactPosition Position);

public sealed record EncounterPair(SourcePlacement Specter, SourcePlacement Skeleton);

public sealed record PairingReport(
    IReadOnlyList<EncounterPair> ValidPairs,
    int UnmatchedSpecters,
    int UnmatchedSkeletons,
    int AmbiguousPairings)
{
    public int ErrorCount => UnmatchedSpecters + UnmatchedSkeletons + AmbiguousPairings;
}

public enum WinningRecordState
{
    Available,
    Missing,
    Deleted,
    UnexpectedBase
}

public sealed record PlannedEncounter(
    FormKey SourceFormKey,
    bool Selected,
    ulong Score,
    WinningRecordState WinningState);
