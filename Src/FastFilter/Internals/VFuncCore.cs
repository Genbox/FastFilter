// SPDX-License-Identifier: MIT
// Copyright (c) 2023, 2025 Inria
// Copyright (c) 2023, 2024, 2025, 2026 Sebastiano Vigna
// Copyright (c) 2023 Tommaso Fontana
// Copyright (c) 2024 Michele Andreata
// Copyright (c) 2025 Dario Moschetti
//
// Permission is hereby granted, free of charge, to any
// person obtaining a copy of this software and associated
// documentation files (the "Software"), to deal in the
// Software without restriction, including without
// limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of
// the Software, and to permit persons to whom the Software
// is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice
// shall be included in all copies or substantial portions
// of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF
// ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT
// SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR
// IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//
// Adapted from sux VFunc/VFilter; see THIRD-PARTY-NOTICES.txt.
// Modified: managed shards with independent retries, serial peeling,
// dense Gaussian core solving, and portable packed-word storage.

using System.Numerics;
using static Genbox.FastFilter.Internals.StaticFilterHash;

namespace Genbox.FastFilter.Internals;

internal sealed class VFuncCore
{
    private readonly ulong _mask;

    internal VFuncCore(ulong[] keys, ulong[] values, int valueBits, ulong seed)
    {
        if (keys.Length != values.Length)
            throw new ArgumentException("Keys and values must have equal lengths.", nameof(values));
        if (valueBits < 1 || valueBits > 64)
            throw new ArgumentOutOfRangeException(nameof(valueBits));
        Count = keys.Length;
        BitWidth = valueBits;
        _mask = ulong.MaxValue >> (64 - valueBits);
        int shards = 1;
        while ((long)shards * 4096 < keys.Length)
            shards *= 2;
        ShardCount = shards;
        ShardSeeds = new ulong[shards];

        for (int attempt = 0; attempt < 128; attempt++)
        {
            Seed = Mix(unchecked(seed + (ulong)attempt));
            List<int>[] shardKeys = new List<int>[shards];
            for (int shard = 0; shard < shards; shard++)
                shardKeys[shard] = new List<int>();
            for (int index = 0; index < keys.Length; index++)
                shardKeys[Shard(keys[index])].Add(index);
            int maximum = 1;
            foreach (List<int> list in shardKeys)
                maximum = Math.Max(maximum, list.Count);
            SegmentLength = 1 << Math.Max(1, (int)Math.Floor(0.85 * Math.Log(maximum)));
            double expansion = maximum <= 5000 ? 1.23 : 1.125;
            int capacity = (int)Math.Ceiling(expansion * maximum);
            SegmentCount = Math.Max(1, (((capacity + SegmentLength) - 1) / SegmentLength) - 2);
            int vertices = checked((SegmentCount + 2) * SegmentLength);
            long cells = checked((long)vertices * shards);
            Data = new ulong[checked((int)(((cells * valueBits) + 63) / 64))];
            int gaussianEquations = 0;
            bool success = true;

            for (int shard = 0; shard < shards; shard++)
            {
                // Retry each shard independently; requiring every shard to succeed
                // with one shared edge seed becomes unlikely as the shard count grows.
                bool solved = false;

                for (int shardAttempt = 0; shardAttempt < 128; shardAttempt++)
                {
                    ShardSeeds[shard] = (ulong)shardAttempt;
                    if (!SolveShard(keys, values, shardKeys[shard], shard, vertices, out int equations))
                        continue;
                    gaussianEquations += equations;
                    solved = true;
                    break;
                }

                if (!solved)
                {
                    success = false;
                    break;
                }
            }

            if (success)
            {
                GaussianEquationCount = gaussianEquations;
                return;
            }
        }

        throw new InvalidOperationException("Could not solve the fuse equations in 128 attempts.");
    }

    internal VFuncCore(int count, int bitWidth, ulong seed, int segmentLength, int segmentCount, int shardCount, ulong[] data, ulong[] shardSeeds)
    {
        Count = count;
        BitWidth = bitWidth;
        Seed = seed;
        SegmentLength = segmentLength;
        SegmentCount = segmentCount;
        ShardCount = shardCount;
        ShardSeeds = shardSeeds;
        Data = data;
        _mask = ulong.MaxValue >> (64 - bitWidth);
    }

    internal ulong Seed { get; }
    internal int BitWidth { get; }
    internal int SegmentLength { get; }
    internal int SegmentCount { get; }
    internal int ShardCount { get; }
    internal int Count { get; }
    internal ulong[] Data { get; }
    internal ulong[] ShardSeeds { get; }
    internal int GaussianEquationCount { get; }

    internal ulong Get(ulong key)
    {
        int shard = Shard(key);
        Edge(key, shard, out int first, out int second, out int third);
        int offset = checked(shard * ((SegmentCount + 2) * SegmentLength));
        if (BitWidth == 8)
            return (ulong)(ReadByte(offset + first) ^ ReadByte(offset + second) ^ ReadByte(offset + third));
        return Read(offset + first) ^ Read(offset + second) ^ Read(offset + third);
    }

    internal uint GetMemoryUsage() => checked((uint)(((long)Data.Length * 8) + ((long)ShardSeeds.Length * 8) + 40));

    private int Shard(ulong key) => (int)(Mix(key ^ Seed ^ 0xa0761d6478bd642fUL) & (ulong)(ShardCount - 1));

    private void Edge(ulong key, int shard, out int first, out int second, out int third)
    {
        ulong hash = Mix(unchecked(key + Seed + ShardSeeds[shard]));
        first = Reduce(hash, SegmentCount * SegmentLength);
        second = (first + SegmentLength) ^ (unchecked((int)hash) & (SegmentLength - 1));
        int shift = BitOperations.TrailingZeroCount((ulong)SegmentLength);
        third = (second + SegmentLength) ^ (unchecked((int)(hash >> shift)) & (SegmentLength - 1));
    }

    private bool SolveShard(ulong[] keys, ulong[] values, List<int> indices, int shard, int vertices, out int gaussianEquations)
    {
        int[] edges = new int[checked(indices.Count * 3)];
        int[] degrees = new int[vertices];
        int[] incident = new int[vertices];
        bool[] active = new bool[indices.Count];

        for (int edge = 0; edge < indices.Count; edge++)
        {
            Edge(keys[indices[edge]], shard, out int first, out int second, out int third);
            edges[3 * edge] = first;
            edges[(3 * edge) + 1] = second;
            edges[(3 * edge) + 2] = third;
            active[edge] = true;

            for (int part = 0; part < 3; part++)
            {
                int vertex = edges[(3 * edge) + part];
                degrees[vertex]++;
                incident[vertex] ^= edge;
            }
        }

        int[] queue = new int[vertices];
        int head = 0;
        int tail = 0;

        for (int vertex = 0; vertex < vertices; vertex++)
        {
            if (degrees[vertex] == 1)
                queue[tail++] = vertex;
        }

        (int Edge, int Vertex)[] peeled = new (int, int)[indices.Count];
        int peeledCount = 0;

        while (head < tail)
        {
            int vertex = queue[head++];
            if (degrees[vertex] != 1)
                continue;
            int edge = incident[vertex];
            active[edge] = false;
            peeled[peeledCount++] = (edge, vertex);

            for (int part = 0; part < 3; part++)
            {
                int neighbor = edges[(3 * edge) + part];
                degrees[neighbor]--;
                incident[neighbor] ^= edge;
                if (degrees[neighbor] == 1)
                    queue[tail++] = neighbor;
            }
        }

        gaussianEquations = indices.Count - peeledCount;
        ulong[] solution = new ulong[vertices];
        if (gaussianEquations > 0)
        {
            ulong[][] pivots = new ulong[vertices][];
            ulong[] results = new ulong[vertices];
            int words = (vertices + 63) / 64;

            for (int edge = 0; edge < indices.Count; edge++)
            {
                if (!active[edge])
                    continue;
                ulong[] row = new ulong[words];

                for (int part = 0; part < 3; part++)
                {
                    int vertex = edges[(3 * edge) + part];
                    row[vertex >> 6] ^= 1UL << (vertex & 63);
                }

                ulong result = values[indices[edge]] & _mask;
                int word = 0;

                while (word < words)
                {
                    if (row[word] == 0)
                    {
                        word++;
                        continue;
                    }

                    int pivot = (word * 64) + BitOperations.TrailingZeroCount(row[word]);
                    ulong[]? other = pivots[pivot];

                    if (other == null)
                    {
                        pivots[pivot] = row;
                        results[pivot] = result;
                        break;
                    }

                    for (int column = word; column < words; column++)
                        row[column] ^= other[column];
                    result ^= results[pivot];
                }

                if (word == words && result != 0)
                    return false;
            }

            // Unassigned solution entries are zero, so each row can include its own pivot.
            for (int pivot = vertices - 1; pivot >= 0; pivot--)
            {
                ulong[]? row = pivots[pivot];
                if (row == null)
                    continue;
                ulong result = results[pivot];

                for (int word = pivot >> 6; word < words; word++)
                {
                    ulong bits = row[word];

                    while (bits != 0)
                    {
                        result ^= solution[(word * 64) + BitOperations.TrailingZeroCount(bits)];
                        bits &= bits - 1;
                    }
                }

                solution[pivot] = result;
            }
        }

        for (int index = peeledCount - 1; index >= 0; index--)
        {
            (int edge, int vertex) = peeled[index];
            ulong result = values[indices[edge]] & _mask;
            for (int part = 0; part < 3; part++)
                result ^= solution[edges[(3 * edge) + part]];
            solution[vertex] = result;
        }

        int offset = shard * vertices;
        for (int vertex = 0; vertex < vertices; vertex++)
            Write(offset + vertex, solution[vertex]);
        return true;
    }

    private byte ReadByte(int index) => (byte)(Data[index >> 3] >> ((index & 7) * 8));

    private ulong Read(int index)
    {
        long bit = (long)index * BitWidth;
        int word = (int)(bit >> 6);
        int shift = (int)(bit & 63);
        ulong result = Data[word] >> shift;
        if (shift + BitWidth > 64)
            result |= Data[word + 1] << (64 - shift);
        return result & _mask;
    }

    private void Write(int index, ulong value)
    {
        long bit = (long)index * BitWidth;
        int word = (int)(bit >> 6);
        int shift = (int)(bit & 63);
        value &= _mask;
        Data[word] |= value << shift;
        if (shift + BitWidth > 64)
            Data[word + 1] |= value >> (64 - shift);
    }
}