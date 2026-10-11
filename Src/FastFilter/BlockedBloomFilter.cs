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

using System.Runtime.CompilerServices;
using Genbox.FastFilter.Abstracts;
using static Genbox.FastFilter.Internals.Common;

namespace Genbox.FastFilter;

/// <summary>
/// Much faster than an ordinary Bloom filter. False-positive rate is comparable (within 1%) if you set bitsPerKey to +2 compared to Bloom filters.
/// </summary>
public class BlockedBloomFilter<T> : IFilter<T> where T : notnull
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

    /// <summary>Creates an empty filter sized for the expected number of keys.</summary>
    /// <param name="capacity">The expected number of keys.</param>
    /// <param name="bitsPerKey">The number of bits to allocate per key.</param>
    public BlockedBloomFilter(int capacity, int bitsPerKey = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerKey);

        long bits = (long)capacity * bitsPerKey;
        long buckets = bits / 64;
        long arrayLength = buckets + 8;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayLength, Array.MaxLength, nameof(capacity));
        _arrayLength = (uint)arrayLength; //Genbox: We save the array length to avoid virtual calls to array.Length
        _array = new ulong[_arrayLength];
        _seed = GetSeed();
    }

    /// <summary>Creates a filter containing the supplied keys.</summary>
    /// <param name="data">The keys to add.</param>
    /// <param name="bitsPerKey">The number of bits to allocate per key.</param>
    public BlockedBloomFilter(ReadOnlySpan<T> data, int bitsPerKey = 10) : this(data.Length, bitsPerKey)
    {
        foreach (ref readonly T item in data)
            Add(item);
    }

    /// <inheritdoc />
    public void Add(T key)
    {
        ulong hash = MixSplit(key.GetHashCode(), _seed);
        ulong idx = FastRange32((uint)hash, _arrayLength);
        ulong m1 = 1UL << (int)hash;
        ulong m2 = 1UL << (int)(hash >> 8);
        ulong m = m1 | m2;
        _array[idx] |= m;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public uint GetMemoryUsage() => (_arrayLength * sizeof(ulong)) + (uint)Unsafe.SizeOf<BlockedBloomFilter<T>>();

    /// <inheritdoc />
    public override string ToString() => "BlockedBloomFilter";
}