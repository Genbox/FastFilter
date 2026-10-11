// SPDX-License-Identifier: Apache-2.0
// Copyright 2018 the fastfilter_cpp authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     https://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// See LICENSES/Apache-2.0.txt and THIRD-PARTY-NOTICES.txt.
//
// Adapted from FastFilter/fastfilter_cpp (src/xorfilter).
// Modified: C# port with generic fingerprints, managed scratch storage, and revised construction.

using System.Numerics;
using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using static System.Math;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

/// <summary>
/// A generic variant of the binary fuse filter. Only for advanced use-cases such as 32/64bit binary fuse filters. Use <see cref="BinaryFuse8Filter{T}" /> or <see cref="BinaryFuse16Filter{T}" /> instead.
/// </summary>
public class BinaryFuseFilter<T, T2> : ImmutableFilter<T> where T : notnull where T2 : INumberBase<T2>, IBitwiseOperators<T2, T2, T2>
{
    // This is the 3-wise variant of the binary fuse filter.
    // Built with generic math to support 8bit and 16bit variants using the same codebase.
    // Generics is also used to enable the user to use any key type they want.
    //
    // Original C implementation by Thomas Mueller Graf and Daniel Lemire
    // - Paper: https://arxiv.org/abs/2201.01174
    // - Source code: https://github.com/FastFilter/fastfilter_cpp/

    private const uint XorMaxIterations = 100; // probability of success should always be > 0.5 so 100 iterations is highly unlikely
    private readonly uint _arrayLength;
    private readonly T2[] _fingerprints;
    private readonly uint _segmentCountLength;
    private readonly uint _segmentLength;
    private readonly uint _segmentLengthMask;

    private ulong _seed;

    /// <summary>Creates an immutable filter containing the supplied keys.</summary>
    /// <param name="data">The keys to add.</param>
    protected BinaryFuseFilter(ReadOnlySpan<T> data)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(data.Length, nameof(data));

        uint size = (uint)data.Length;

        _segmentLength = 1U << (int)Floor((Log(size) / Log(3.33f)) + 2.25f);

        //Genbox: This is the same as 1 << 18, which limits the segment length to 18 bits
        if (_segmentLength > 262144)
            _segmentLength = 262144;

        _segmentLengthMask = _segmentLength - 1;

        double sizeFactor = size <= 1 ? 0 : Max(1.125, 0.875 + ((0.25 * Log(1000000.0)) / Log(size)));
        uint capacity = size <= 1 ? 0 : (uint)Round(size * sizeFactor, MidpointRounding.AwayFromZero);
        uint segmentCount = ((capacity + _segmentLength) - 1) / _segmentLength;
        segmentCount = segmentCount > 2 ? segmentCount - 2 : 1;

        _arrayLength = (segmentCount + 2) * _segmentLength;
        _segmentCountLength = segmentCount * _segmentLength;
        _fingerprints = new T2[_arrayLength];

        Populate(data, segmentCount);
    }

    /// <inheritdoc />
    public uint GetMemoryUsage() => (_arrayLength * (uint)Unsafe.SizeOf<T2>()) + (uint)Unsafe.SizeOf<BinaryFuseFilter<T, T2>>();

    /// <inheritdoc />
    public bool Contains(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        T2 f = Fingerprint(hash);

        uint h0 = (uint)BigMul(hash, _segmentCountLength, out _);
        uint h1 = h0 + _segmentLength;
        uint h2 = h1 + _segmentLength;

        h1 ^= (uint)(hash >> 18) & _segmentLengthMask;
        h2 ^= (uint)hash & _segmentLengthMask;

        f ^= _fingerprints[h0] ^ _fingerprints[h1] ^ _fingerprints[h2];
        return f == T2.Zero;
    }

    private void Populate(ReadOnlySpan<T> keys, uint segmentCount)
    {
        uint size = (uint)keys.Length;
        _seed = GetSeed();

        ulong[] reverseOrder = new ulong[size + 1];
        uint[] alone = new uint[_arrayLength];
        byte[] t2Count = new byte[_arrayLength];
        byte[] reverseH = new byte[size];
        ulong[] t2Hash = new ulong[_arrayLength];

        uint block = BitOperations.RoundUpToPowerOf2(Max(2U, segmentCount));
        int blockBits = BitOperations.Log2(block);
        uint[] startPos = new uint[block];
        Span<uint> h012 = stackalloc uint[5];

        reverseOrder[size] = 1;
        bool success = false;

        for (int loop = 0; loop < XorMaxIterations; ++loop)
        {
            // important: i * size would overflow as a 32-bit number in some cases.
            for (uint i = 0; i < block; i++)
                startPos[i] = (uint)(((ulong)i * size) >> blockBits);

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

            bool error = false;
            uint duplicates = 0;

            for (uint i = 0; i < size; i++)
            {
                ulong hash = reverseOrder[i];
                uint h0 = Hash(0, hash);
                t2Count[h0] += 4;
                t2Hash[h0] ^= hash;
                uint h1 = Hash(1, hash);
                t2Count[h1] += 4;
                t2Count[h1] ^= 1;
                t2Hash[h1] ^= hash;
                uint h2 = Hash(2, hash);
                t2Count[h2] += 4;
                t2Hash[h2] ^= hash;
                t2Count[h2] ^= 2;

                if ((t2Hash[h0] & t2Hash[h1] & t2Hash[h2]) == 0 &&
                    ((t2Hash[h0] == 0 && t2Count[h0] == 8) || (t2Hash[h1] == 0 && t2Count[h1] == 8) || (t2Hash[h2] == 0 && t2Count[h2] == 8)))
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

                error |= t2Count[h0] < 4 || t2Count[h1] < 4 || t2Count[h2] < 4;
            }

            if (error)
            {
                Array.Clear(reverseOrder, 0, (int)size);
                Array.Clear(t2Count);
                Array.Clear(t2Hash);
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

                if (t2Count[index] >> 2 != 1)
                    continue;

                ulong hash = t2Hash[index];

                h012[1] = Hash(1, hash);
                h012[2] = Hash(2, hash);
                h012[3] = Hash(0, hash);
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

            if (stackSize + duplicates == size)
            {
                // success
                size = stackSize;
                success = true;
                break;
            }

            if (duplicates > 0)
            {
                HashSet<int> uniqueHashes = new HashSet<int>(keys.Length);
                T[] uniqueKeys = new T[keys.Length];
                int uniqueCount = 0;

                for (int i = 0; i < keys.Length; i++)
                {
                    if (uniqueHashes.Add(keys[i].GetHashCode()))
                        uniqueKeys[uniqueCount++] = keys[i];
                }

                size = (uint)uniqueCount;
                keys = uniqueKeys.AsSpan(0, uniqueCount);
            }

            Array.Clear(reverseOrder, 0, (int)size);
            Array.Clear(t2Count);
            Array.Clear(t2Hash);
            _seed = (ulong)Random.Shared.NextInt64();
        }

        if (!success)
            throw new InvalidOperationException("Failed to construct binary fuse filter within the iteration limit.");

        for (uint remaining = size; remaining > 0; remaining--)
        {
            uint i = remaining - 1;

            // the hash of the key we insert next
            ulong hash = reverseOrder[i];
            T2 xor2 = Fingerprint(hash);
            byte found = reverseH[i];
            h012[0] = Hash(0, hash);
            h012[1] = Hash(1, hash);
            h012[2] = Hash(2, hash);
            h012[3] = h012[0];
            h012[4] = h012[1];
            _fingerprints[h012[found]] = xor2 ^ _fingerprints[h012[found + 1]] ^ _fingerprints[h012[found + 2]];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Mod3(byte x) => x > 2 ? (byte)(x - 3) : x;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint Hash(int index, ulong hash)
    {
        ulong h = BigMul(hash, _segmentCountLength, out _);
        h += (ulong)index * _segmentLength;

        // keep the lower 36 bits
        ulong hh = hash & ((1UL << 36) - 1);

        // index 0: right shift by 36; index 1: right shift by 18; index 2: no shift
        h ^= (hh >> (36 - (18 * index))) & _segmentLengthMask;
        return (uint)h;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T2 Fingerprint(ulong hash) => T2.CreateTruncating(hash ^ (hash >> 32));

    /// <inheritdoc />
    public override string ToString() => $"BinaryFuse{Unsafe.SizeOf<T2>() * 8}Filter";
}