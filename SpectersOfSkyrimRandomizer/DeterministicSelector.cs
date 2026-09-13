using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Mutagen.Bethesda.Plugins;

namespace SpectersOfSkyrimRandomizer;

/// <summary>
/// Implements the frozen SpectersOfSkyrimRandomizer/v1 selection contract.
/// </summary>
public static class DeterministicSelector
{
    public const string AlgorithmVersion = "v1";
    public const string Domain = "SpectersOfSkyrimRandomizer/v1";

    private static readonly byte[] DomainBytes = Encoding.UTF8.GetBytes(Domain);

    public static bool IsSelected(FormKey sourceFormKey, int seed, double probability)
    {
        ValidateProbability(probability);

        if (probability == 0.0)
        {
            return false;
        }

        if (probability == 100.0)
        {
            return true;
        }

        var score = ComputeScore(sourceFormKey, seed);
        var unitIntervalValue = (score >> 11) * (1.0 / 9_007_199_254_740_992.0);
        return unitIntervalValue < probability / 100.0;
    }

    public static ulong ComputeScore(FormKey sourceFormKey, int seed)
    {
        var pluginFileName = sourceFormKey.ModKey.FileName.String.ToLowerInvariant();
        var pluginBytes = Encoding.UTF8.GetBytes(pluginFileName);
        var serialized = new byte[
            sizeof(uint) + DomainBytes.Length +
            sizeof(int) +
            sizeof(uint) + pluginBytes.Length +
            sizeof(uint)];

        var offset = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(serialized.AsSpan(offset), (uint)DomainBytes.Length);
        offset += sizeof(uint);
        DomainBytes.CopyTo(serialized, offset);
        offset += DomainBytes.Length;
        BinaryPrimitives.WriteInt32LittleEndian(serialized.AsSpan(offset), seed);
        offset += sizeof(int);
        BinaryPrimitives.WriteUInt32LittleEndian(serialized.AsSpan(offset), (uint)pluginBytes.Length);
        offset += sizeof(uint);
        pluginBytes.CopyTo(serialized, offset);
        offset += pluginBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(serialized.AsSpan(offset), sourceFormKey.ID);

        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(serialized, hash);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    public static void ValidateProbability(double probability)
    {
        if (!double.IsFinite(probability) || probability < 0.0 || probability > 100.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                probability,
                "Probability must be a finite value from 0 through 100 inclusive.");
        }
    }
}
