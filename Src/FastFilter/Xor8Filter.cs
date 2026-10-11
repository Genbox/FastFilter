// SPDX-License-Identifier: Apache-2.0
// Copyright 2019 Daniel Lemire
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
// Adapted from FastFilter/xor_singleheader.
// Modified: C# port with managed arrays, caller-supplied 64-bit hashes, fingerprint mixing, and sizing.

using System.Numerics;
using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter;

/// <summary>An immutable three-part XOR filter with eight-bit fingerprints.</summary>
/// <typeparam name="T">The key type.</typeparam>
public sealed class Xor8Filter<T> : ImmutableFilter<T> where T : notnull
{
    private readonly int _blockLength;
    private readonly byte[] _data;
    private readonly Func<T, ulong> _hash;
    private readonly ulong _seed;

    /// <summary>Builds a filter. The hash delegate must provide uniformly distributed, deterministic 64-bit hashes.</summary>
    /// <param name="keys">The keys to include.</param>
    /// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
    public Xor8Filter(ReadOnlySpan<T> keys, Func<T, ulong> hash)
    {
        _hash = hash;
        ulong[] signatures = StaticFilterHash.Signatures(keys, hash);
        _blockLength = (int)Math.Ceiling(((1.23 * signatures.Length) + 32) / 3);
        _data = new byte[signatures.Length == 0 ? 0 : 3 * _blockLength];
        if (signatures.Length == 0)
            return;

        for (int attempt = 0; attempt < 128; attempt++)
        {
            _seed = StaticFilterHash.Mix(1UL + (ulong)attempt);
            if (Populate(signatures))
                return;
        }

        throw new InvalidOperationException("Could not peel the XOR graph in 128 attempts.");
    }

    /// <inheritdoc />
    public bool Contains(T key)
    {
        if (_data.Length == 0)
            return false;
        ulong hash = StaticFilterHash.Mix(_hash(key) + _seed);
        return (_data[Position(hash, 0)] ^ _data[Position(hash, 1)] ^ _data[Position(hash, 2)]) == StaticFilterHash.Fingerprint(hash);
    }

    /// <summary>Returns the fingerprint payload and scalar fields, excluding managed object headers and the hash delegate.</summary>
    public uint GetMemoryUsage() => checked((uint)_data.Length + 12);

    private int Position(ulong hash, int part) => (part * _blockLength) + StaticFilterHash.Reduce(BitOperations.RotateLeft(hash, part * 21), _blockLength);

    private bool Populate(ulong[] signatures)
    {
        int[] degree = new int[_data.Length];
        ulong[] incident = new ulong[_data.Length];

        foreach (ulong signature in signatures)
        {
            ulong hash = StaticFilterHash.Mix(signature + _seed);

            for (int part = 0; part < 3; part++)
            {
                int position = Position(hash, part);
                degree[position]++;
                incident[position] ^= hash;
            }
        }

        int[] queue = new int[_data.Length];
        int head = 0;
        int tail = 0;

        for (int position = 0; position < degree.Length; position++)
        {
            if (degree[position] == 1)
                queue[tail++] = position;
        }

        (int Position, ulong Hash)[] order = new (int, ulong)[signatures.Length];
        int count = 0;

        while (head < tail)
        {
            int position = queue[head++];
            if (degree[position] != 1)
                continue;
            ulong hash = incident[position];
            order[count++] = (position, hash);

            for (int part = 0; part < 3; part++)
            {
                int neighbor = Position(hash, part);
                degree[neighbor]--;
                incident[neighbor] ^= hash;
                if (degree[neighbor] == 1)
                    queue[tail++] = neighbor;
            }
        }

        if (count != signatures.Length)
            return false;

        for (int index = count - 1; index >= 0; index--)
        {
            (int position, ulong hash) = order[index];
            _data[position] = (byte)(StaticFilterHash.Fingerprint(hash) ^ _data[Position(hash, 0)] ^ _data[Position(hash, 1)] ^ _data[Position(hash, 2)]);
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => "Xor8Filter";
}