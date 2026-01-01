using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

/// <summary>
/// Much faster than an ordinary Bloom filter. False-positive rate is comparable (within 1%) if you set bitsPerKey to +2 compared to Bloom filters.
/// </summary>
public class BlockedBloomFilter<T> : IBloomFilter<T> where T : notnull
{
    // This is a Register Blocked Bloom filter. It performs all the computation inside a single 64bit register.
    // Blocked Bloom filters have great cache locality because they write within a single cache-line.
    //
    // Original C implementation by Thomas Mueller Graf and Daniel Lemire
    // - Paper: https://dl.acm.org/doi/abs/10.14778/3303753.3303757
    // - Source code: https://github.com/FastFilter/fastfilter_cpp/blob/5df1dc5063702945f6958e4bda445dd082aed366/src/bloom/bloom.h#L274

    private readonly ulong[] _array;
    private readonly uint _arrayLength;
    private readonly ulong _seed;

    public BlockedBloomFilter(int capacity, int bitsPerKey = 10)
    {
        ArgumentOutOfRangeException.ThrowIfZero(capacity);
        ArgumentOutOfRangeException.ThrowIfZero(bitsPerKey);

        int bits = capacity * bitsPerKey;
        int buckets = bits / 64;
        _arrayLength = (uint)(buckets + 8); //Genbox: We save the array length to avoid virtual calls to array.Length
        _array = new ulong[_arrayLength];
        _seed = GetSeed();
    }

    public BlockedBloomFilter(ReadOnlySpan<T> data, int bitsPerKey = 10) : this(data.Length, bitsPerKey)
    {
        foreach (ref readonly T item in data)
            Add(item);
    }

    public void Add(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        ulong idx = FastRange32((uint)hash, _arrayLength);
        ulong m1 = 1UL << (int)hash;
        ulong m2 = 1UL << (int)(hash >> 8);
        ulong m = m1 | m2;
        _array[idx] |= m;
    }

    public bool Contains(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        ulong idx = FastRange32((uint)hash, _arrayLength);
        ulong m1 = 1UL << (int)hash;
        ulong m2 = 1UL << (int)(hash >> 8);
        ulong m = m1 | m2;
        ulong bucket = _array[idx];
        return (m & bucket) == m;
    }

    public uint GetMemoryUsage() => (_arrayLength * sizeof(ulong)) + (uint)Unsafe.SizeOf<BlockedBloomFilter<T>>();

    public override string ToString() => "BlockedBloomFilter";
}