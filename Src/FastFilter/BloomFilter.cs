using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

public class BloomFilter<T> : IBloomFilter<T> where T : notnull
{
    // A simple Bloom filter. It uses k number of hash functions.
    //
    // Original C implementation by Thomas Mueller Graf and Daniel Lemire
    // - Paper: https://dl.acm.org/doi/10.1145/362686.362692
    // - Source code: https://github.com/FastFilter/fastfilter_cpp/blob/5df1dc5063702945f6958e4bda445dd082aed366/src/bloom/bloom.h#L98

    private readonly ulong[] _array;
    private readonly uint _arrayLength;
    private readonly byte _k;
    private readonly ulong _seed;

    public BloomFilter(int capacity, uint bitsPerKey = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerKey);

        _k = GetBestK(bitsPerKey);
        uint bitCount = (uint)capacity * bitsPerKey;
        _arrayLength = (bitCount + 63) / 64;
        _array = new ulong[_arrayLength];
        _seed = GetSeed();
    }

    public BloomFilter(ReadOnlySpan<T> data, uint bitsPerKey = 8) : this(data.Length, bitsPerKey)
    {
        foreach (ref readonly T item in data)
            Add(item);
    }

    public void Add(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        ulong a = (hash >> 32) | (hash << 32);

        for (int i = 0; i < _k; i++)
        {
            uint aa = (uint)a;
            _array[FastRange32(aa, _arrayLength)] |= GetBit(aa);
            a += hash;
        }
    }

    public bool Contains(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        ulong a = (hash >> 32) | (hash << 32);

        for (int i = 0; i < _k; i++)
        {
            uint aa = (uint)a;

            if ((_array[FastRange32(aa, _arrayLength)] & GetBit(aa)) == 0)
                return false;

            a += hash;
        }

        return true;
    }

    public uint GetMemoryUsage() => (_arrayLength * sizeof(ulong)) + (uint)Unsafe.SizeOf<BloomFilter<T>>();

    public override string ToString() => "BloomFilter";

    private static byte GetBestK(uint bitsPerItem) => (byte)Math.Max(1, (int)Math.Round(bitsPerItem * Math.Log(2), MidpointRounding.AwayFromZero));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetBit(uint index) => 1UL << (int)(index & 63);
}