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
// Adapted from FastFilter/fastfilter_cpp (src/bloom/bloom.h).
// Modified: C# port with managed arrays, generic keys, seeded hashing, and checked sizing.

using System.Numerics;
using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

/// <summary>A Bloom filter with a configurable number of bits per key.</summary>
/// <typeparam name="T">The type of keys stored in the filter.</typeparam>
public class BloomFilter<T> : IFilter<T> where T : notnull
{
    // A simple Bloom filter. It uses k number of hash functions.
    //
    // Original C implementation by Thomas Mueller Graf and Daniel Lemire
    // - Paper: https://dl.acm.org/doi/10.1145/362686.362692
    // - Source code: https://github.com/FastFilter/fastfilter_cpp/blob/5df1dc5063702945f6958e4bda445dd082aed366/src/bloom/bloom.h#L98

    private readonly ulong[] _array;
    private readonly uint _arrayLength;
    private readonly Func<T, ulong> _hash;
    private readonly byte _k;
    private readonly ulong _seed;

    /// <summary>Creates an empty filter sized for the expected number of keys.</summary>
    /// <param name="capacity">The expected number of keys.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    /// <param name="bitsPerKey">The number of bits to allocate per key.</param>
    public BloomFilter(int capacity, Func<T, ulong> hash, uint bitsPerKey = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerKey);

        _k = GetBestK(bitsPerKey);
        ulong bitCount = (ulong)capacity * bitsPerKey;
        ulong arrayLength = (bitCount + 63) / 64;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayLength, (ulong)Array.MaxLength, nameof(capacity));
        _arrayLength = (uint)arrayLength;
        _array = new ulong[_arrayLength];
        ArgumentNullException.ThrowIfNull(hash);
        _hash = hash;
        _seed = StaticFilterHash.Mix(1UL);
    }

    /// <summary>Creates a filter containing the supplied keys.</summary>
    /// <param name="data">The keys to add.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    /// <param name="bitsPerKey">The number of bits to allocate per key.</param>
    public BloomFilter(ReadOnlySpan<T> data, Func<T, ulong> hash, uint bitsPerKey = 8) : this(data.Length, hash, bitsPerKey)
    {
        foreach (ref readonly T item in data)
            Add(item);
    }

    /// <inheritdoc />
    public void Add(T key)
    {
        ulong hash = StaticFilterHash.Mix(_hash(key) + _seed);
        ulong a = BitOperations.RotateLeft(hash, 32);

        for (int i = 0; i < _k; i++)
        {
            uint aa = (uint)a;
            _array[FastRange32(aa, _arrayLength)] |= GetBit(aa);
            a += hash;
        }
    }

    /// <inheritdoc />
    public bool Contains(T key)
    {
        ulong hash = StaticFilterHash.Mix(_hash(key) + _seed);
        ulong a = BitOperations.RotateLeft(hash, 32);

        for (int i = 0; i < _k; i++)
        {
            uint aa = (uint)a;

            if ((_array[FastRange32(aa, _arrayLength)] & GetBit(aa)) == 0)
                return false;

            a += hash;
        }

        return true;
    }

    /// <inheritdoc />
    public uint GetMemoryUsage() => (_arrayLength * sizeof(ulong)) + (uint)Unsafe.SizeOf<BloomFilter<T>>();

    /// <inheritdoc />
    public override string ToString() => "BloomFilter";

    private static byte GetBestK(uint bitsPerItem)
    {
        double bestK = Math.Round(bitsPerItem * Math.Log(2), MidpointRounding.AwayFromZero);

        // DPR-029: prevent the probe count from wrapping the backing byte to zero.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bestK, byte.MaxValue, nameof(bitsPerItem));
        return (byte)Math.Max(1, (int)bestK);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong GetBit(uint index) => 1UL << (int)(index & 63);
}