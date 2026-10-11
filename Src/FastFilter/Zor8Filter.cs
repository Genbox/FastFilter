using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter;

/// <summary>ZOR filter: an eight-bit abandonment layer and a lossless sixteen-bit fuse remainder.</summary>
/// <typeparam name="T">The key type.</typeparam>
public sealed class Zor8Filter<T> : ImmutableFilter<T> where T : notnull
{
    private readonly Func<T, ulong> _hash;
    private readonly ZorLayer _main;
    private readonly ZorLayer _remainder;

    /// <summary>Builds both layers using a deterministic, uniformly distributed 64-bit hash delegate.</summary>
    /// <param name="keys">The keys to include.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    public Zor8Filter(ReadOnlySpan<T> keys, Func<T, ulong> hash)
    {
        _hash = hash;
        _main = new ZorLayer(StaticFilterHash.Signatures(keys, hash), 1UL, true, out ulong[] abandoned);
        RemainderCount = abandoned.Length;
        _remainder = new ZorLayer(abandoned, StaticFilterHash.Mix(2), false, out _);
    }

    /// <summary>Gets the number of keys represented in the remainder layer.</summary>
    public int RemainderCount { get; }

    /// <inheritdoc />
    public bool Contains(T key)
    {
        ulong signature = _hash(key);
        return _main.Contains(signature) || _remainder.Contains(signature);
    }

    /// <summary>Returns stored payload and scalar bytes, excluding managed headers and the hash delegate.</summary>
    public uint GetMemoryUsage() => checked(_main.MemoryUsage + _remainder.MemoryUsage + 4);

    /// <inheritdoc />
    public override string ToString() => "Zor8Filter";
}