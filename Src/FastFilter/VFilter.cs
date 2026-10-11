using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter;

/// <summary>A static VFilter using sharded fuse equations, Gaussian core solving, and eight-bit fingerprints.</summary>
/// <typeparam name="T">The key type.</typeparam>
public sealed class VFilter<T> : ImmutableFilter<T> where T : notnull
{
    private readonly VFuncCore _core;
    private readonly Func<T, ulong> _hash;

    /// <summary>Builds the filter using a deterministic, uniformly distributed 64-bit hash delegate.</summary>
    /// <param name="keys">The keys to include.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    public VFilter(ReadOnlySpan<T> keys, Func<T, ulong> hash)
    {
        _hash = hash;
        ulong[] signatures = StaticFilterHash.Signatures(keys, hash);
        ulong[] values = new ulong[signatures.Length];
        for (int index = 0; index < values.Length; index++)
            values[index] = StaticFilterHash.Fingerprint(signatures[index]);
        _core = new VFuncCore(signatures, values, 8, 1UL);
    }

    /// <inheritdoc />
    public bool Contains(T key)
    {
        if (_core.Count == 0)
            return false;
        ulong signature = _hash(key);
        return _core.Get(signature) == StaticFilterHash.Fingerprint(signature);
    }

    /// <summary>Returns stored payload and scalar bytes, excluding managed headers and the hash delegate.</summary>
    public uint GetMemoryUsage() => _core.GetMemoryUsage();

    /// <inheritdoc />
    public override string ToString() => "VFilter";
}