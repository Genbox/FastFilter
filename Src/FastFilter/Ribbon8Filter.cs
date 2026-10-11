using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter;

/// <summary>An immutable 128-bit banded Ribbon filter with eight-bit fingerprints.</summary>
/// <typeparam name="T">The key type.</typeparam>
public sealed class Ribbon8Filter<T> : ImmutableFilter<T> where T : notnull
{
    private readonly Func<T, ulong> _hash;
    private readonly RibbonLayer _layer;

    /// <summary>Builds a filter using a deterministic, uniformly distributed 64-bit hash delegate.</summary>
    /// <param name="keys">The keys to include.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    public Ribbon8Filter(ReadOnlySpan<T> keys, Func<T, ulong> hash)
    {
        _hash = hash;
        _layer = new RibbonLayer(StaticFilterHash.Signatures(keys, hash), 1UL, false);
    }

    /// <inheritdoc />
    public bool Contains(T key) => _layer.Contains(_hash(key));

    /// <summary>Returns stored payload and scalar bytes, excluding managed headers and the hash delegate.</summary>
    public uint GetMemoryUsage() => _layer.MemoryUsage;

    /// <inheritdoc />
    public override string ToString() => "Ribbon8Filter";
}