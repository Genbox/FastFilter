using System.Numerics;
using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using static System.Math;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

/// <summary>
/// A generic variant of the binary fuse filter. Only for advanced use-cases such as 32/64bit binary fuse filters. Use <see cref="BinaryFuse8Filter{T}"/> or <see cref="BinaryFuse16Filter{T}"/> instead.
/// </summary>
public class BinaryFuseFilter<T, T2> : IBloomFilter<T> where T : notnull where T2 : INumberBase<T2>, IBitwiseOperators<T2, T2, T2>
{
    // This is the 3-wise variant of the binary fuse filter.
    // Built with generic math to support 8bit and 16bit variants using the same codebase.
    // Generics is also used to enable the user to use any key type they want.
    //
    // Original C implementation by Thomas Mueller Graf and Daniel Lemire
    // - Paper: https://arxiv.org/abs/2201.01174
    // - Source code: https://github.com/FastFilter/fastfilter_cpp/

    private const uint XorMaxIterations = 100; // probability of success should always be > 0.5 so 100 iterations is highly unlikely

    private ulong _seed;
    private readonly uint _segmentLength;
    private readonly uint _segmentLengthMask;
    private readonly uint _segmentCount;
    private readonly uint _segmentCountLength;
    private readonly uint _arrayLength;
    private readonly T2[] _fingerprints;

    protected BinaryFuseFilter(ReadOnlySpan<T> data)
    {
        ArgumentOutOfRangeException.ThrowIfZero(data.Length);

        uint size = (uint)data.Length;

        _segmentLength = size == 0 ? 4 : 1U << (int)Floor((Log(size) / Log(3.33f)) + 2.25f);

        //Genbox: This is the same as 1 << 18, which limits the segment length to 18 bits
        if (_segmentLength > 262144)
            _segmentLength = 262144;

        _segmentLengthMask = _segmentLength - 1;

        double sizeFactor = size <= 1 ? 0 : Max(1.125, 0.875 + (0.25 * Log(1000000.0) / Log(size)));
        uint capacity = size <= 1 ? 0 : (uint)Round(size * sizeFactor, MidpointRounding.AwayFromZero);
        uint initSegmentCount = ((capacity + _segmentLength - 1) / _segmentLength) - 2;

        _arrayLength = (initSegmentCount + 2) * _segmentLength;
        _segmentCount = (_arrayLength + _segmentLength - 1) / _segmentLength;

        if (_segmentCount <= 2)
            _segmentCount = 1;
        else
            _segmentCount -= 2;

        _arrayLength = (_segmentCount + 2) * _segmentLength;
        _segmentCountLength = _segmentCount * _segmentLength;
        _fingerprints = new T2[_arrayLength];

        Populate(data);
    }

    public uint GetMemoryUsage() => (_arrayLength * (uint)Unsafe.SizeOf<T2>()) + (uint)Unsafe.SizeOf<BinaryFuseFilter<T, T2>>();

    public void Add(T key) => throw new NotSupportedException("Not possible to add items after the initial construction");

    public bool Contains(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        T2 f = Fingerprint(hash);

        uint h0 = (uint)MulHi(hash, _segmentCountLength);
        uint h1 = h0 + _segmentLength;
        uint h2 = h1 + _segmentLength;

        h1 ^= (uint)(hash >> 18) & _segmentLengthMask;
        h2 ^= (uint)hash & _segmentLengthMask;

        //TODO: Not using checked faster?
        f ^= T2.CreateChecked(_fingerprints[h0]) ^ T2.CreateChecked(_fingerprints[h1]) ^ T2.CreateChecked(_fingerprints[h2]);
        return f == T2.Zero;
    }

    private void Populate(ReadOnlySpan<T> keys)
    {
        uint size = (uint)keys.Length;
        _seed = GetSeed();

        ulong[] reverseOrder = new ulong[size + 1];
        uint[] alone = new uint[_arrayLength];
        byte[] t2Count = new byte[_arrayLength];
        byte[] reverseH = new byte[size];
        ulong[] t2Hash = new ulong[_arrayLength];

        int blockBits = 1;
        while (1U << blockBits < _segmentCount)
            blockBits++;

        uint block = 1U << blockBits;
        uint[] startPos = new uint[block];
        uint[] h012 = new uint[5];

        reverseOrder[size] = 1;
        for (int loop = 0; loop < XorMaxIterations; ++loop)
        {
            // The probability of this happening is lower than the cosmic-ray probability (i.e., a cosmic ray corrupts your system)
            if (loop + 1 > XorMaxIterations)
                throw new InvalidOperationException("The impossible happened");

            // important: i * size would overflow as a 32-bit number in some cases.
            for (uint i = 0; i < block; i++)
                startPos[i] = (uint)((ulong)i * size) >> blockBits;

            ulong maskblock = block - 1;
            for (int i = 0; i < size; i++) //Genbox: Switched to int here to avoid cast in array indexer below
            {
                ulong hash = MixSplit(keys[i].GetHashCode(), _seed);
                ulong segmentIndex = hash >> (64 - blockBits);
                while (reverseOrder[startPos[segmentIndex]] != 0)
                {
                    segmentIndex++;
                    segmentIndex &= maskblock;
                }
                reverseOrder[startPos[segmentIndex]] = hash;
                startPos[segmentIndex]++;
            }

            int error = 0;
            uint duplicates = 0;
            for (uint i = 0; i < size; i++)
            {
                ulong hash = reverseOrder[i];
                uint h0 = Hash(0, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                t2Count[h0] += 4;
                t2Hash[h0] ^= hash;
                uint h1 = Hash(1, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                t2Count[h1] += 4;
                t2Count[h1] ^= 1;
                t2Hash[h1] ^= hash;
                uint h2 = Hash(2, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                t2Count[h2] += 4;
                t2Hash[h2] ^= hash;
                t2Count[h2] ^= 2;

                if ((t2Hash[h0] & t2Hash[h1] & t2Hash[h2]) == 0)
                {
                    if ((t2Hash[h0] == 0 && t2Count[h0] == 8) || (t2Hash[h1] == 0 && t2Count[h1] == 8) || (t2Hash[h2] == 0 && t2Count[h2] == 8))
                    {
                        duplicates++;
                        t2Count[h0] -= 4;
                        t2Hash[h0] ^= hash;
                        t2Count[h1] -= 4;
                        t2Count[h1] ^= 1;
                        t2Hash[h1] ^= hash;
                        t2Count[h2] -= 4;
                        t2Count[h2] ^= 2;
                        t2Hash[h2] ^= hash;
                    }
                }

                error = t2Count[h0] < 4 ? 1 : error;
                error = t2Count[h1] < 4 ? 1 : error;
                error = t2Count[h2] < 4 ? 1 : error;
            }

            if (error != 0)
            {
                Array.Clear(reverseOrder, 0, (int)size);
                Array.Clear(t2Count, 0, (int)_arrayLength);
                Array.Clear(t2Hash, 0, (int)_arrayLength);
                _seed = GetSeed();
                continue;
            }

            // End of key addition
            uint qsize = 0;

            // Add sets with one key to the queue.
            for (uint i = 0; i < _arrayLength; i++)
            {
                alone[qsize] = i;
                qsize += t2Count[i] >> 2 == 1 ? 1U : 0U;
            }

            uint stackSize = 0;
            while (qsize > 0)
            {
                qsize--;
                uint index = alone[qsize];
                if (t2Count[index] >> 2 == 1)
                {
                    ulong hash = t2Hash[index];

                    //h012[0] = binary_fuse8_hash(0, hash, filter);
                    h012[1] = Hash(1, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                    h012[2] = Hash(2, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                    h012[3] = Hash(0, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
                    h012[4] = h012[1];

                    byte found = (byte)(t2Count[index] & 3);
                    reverseH[stackSize] = found;
                    reverseOrder[stackSize] = hash;
                    stackSize++;
                    uint otherIndex1 = h012[found + 1];
                    alone[qsize] = otherIndex1;
                    qsize += t2Count[otherIndex1] >> 2 == 2 ? 1U : 0U;

                    t2Count[otherIndex1] -= 4;
                    t2Count[otherIndex1] ^= Mod3((byte)(found + 1));
                    t2Hash[otherIndex1] ^= hash;

                    uint otherIndex2 = h012[found + 2];
                    alone[qsize] = otherIndex2;
                    qsize += t2Count[otherIndex2] >> 2 == 2 ? 1U : 0U;
                    t2Count[otherIndex2] -= 4;
                    t2Count[otherIndex2] ^= Mod3((byte)(found + 2));
                    t2Hash[otherIndex2] ^= hash;
                }
            }

            if (stackSize + duplicates == size)
            {
                // success
                size = stackSize;
                break;
            }

            Array.Clear(reverseOrder, 0, (int)size);
            Array.Clear(t2Count, 0, (int)_arrayLength);
            Array.Clear(t2Hash, 0, (int)_arrayLength);
            _seed = (ulong)Random.Shared.NextInt64();
        }

        for (uint i = size - 1; i < size; i--)
        {
            // the hash of the key we insert next
            ulong hash = reverseOrder[i];
            T2 xor2 = Fingerprint(hash);
            byte found = reverseH[i];
            h012[0] = Hash(0, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
            h012[1] = Hash(1, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
            h012[2] = Hash(2, hash, _segmentCountLength, _segmentLength, _segmentLengthMask);
            h012[3] = h012[0];
            h012[4] = h012[1];
            _fingerprints[h012[found]] = xor2 ^ _fingerprints[h012[found + 1]] ^ _fingerprints[h012[found + 2]];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Mod3(byte x) => x > 2 ? (byte)(x - 3) : x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Hash(int index, ulong hash, uint segmentCountLength, uint segmentLength, uint segmentLengthMask)
    {
        ulong h = MulHi(hash, segmentCountLength);
        h += (ulong)index * segmentLength;
        // keep the lower 36 bits
        ulong hh = hash & ((1UL << 36) - 1);
        // index 0: right shift by 36; index 1: right shift by 18; index 2: no shift
        h ^= (hh >> (36 - (18 * index))) & segmentLengthMask;
        return (uint)h;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T2 Fingerprint(ulong hash) => T2.CreateTruncating(hash ^ (hash >> 32));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MulHi(ulong a, ulong b) => BigMul(a, b, out _);

    public override string ToString() => $"BinaryFuse{Unsafe.SizeOf<T2>() * 8}Filter";
}