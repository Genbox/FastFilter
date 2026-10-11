// SPDX-License-Identifier: Apache-2.0
// Copyright (c) Lorenz Hübschle-Schneider
// Copyright (c) Facebook, Inc. and its affiliates.
// All Rights Reserved.
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
// Adapted from lorenzhs/BuRR (construction.hpp, query.hpp, backsubst.hpp).
// Modified: C# port with 128-bit bands, byte cells and thresholds, and bounded recursive bumping.

using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Genbox.FastFilter.Internals;

internal sealed class RibbonLayer
{
    private const int BandWidth = 128;
    private const int BucketSize = 32;
    private readonly byte[] _data;
    private readonly RibbonLayer? _next;
    private readonly ulong _seed;
    private readonly int _starts;
    private readonly byte[] _thresholds;

    internal RibbonLayer(ulong[] signatures, ulong seed, bool bump, int depth = 0)
    {
        if (signatures.Length == 0)
        {
            _data = [];
            _thresholds = [];
            return;
        }

        bool useBumping = bump && depth < 4 && signatures.Length > 128;
        int slots = checked((int)Math.Ceiling(signatures.Length * (useBumping ? 0.98 : 1.10)) + BandWidth);
        _starts = (slots - BandWidth) + 1;
        _data = new byte[slots];
        _thresholds = useBumping ? new byte[((_starts + BucketSize) - 1) / BucketSize] : [];

        for (int attempt = 0; attempt < 128; attempt++)
        {
            _seed = StaticFilterHash.Mix(seed + (ulong)attempt);
            Array.Fill(_thresholds, (byte)BucketSize);
            UInt128[] coefficients = new UInt128[slots];
            byte[] results = new byte[slots];
            List<ulong> bumped = new List<ulong>();
            if (!Band(signatures, coefficients, results, bumped, useBumping))
                continue;
            BackSubstitute(coefficients, results);
            BumpedCount = bumped.Count;
            if (bumped.Count > 0)
                _next = new RibbonLayer(bumped.ToArray(), StaticFilterHash.Mix(_seed + 1), true, depth + 1);
            return;
        }

        throw new InvalidOperationException("Could not solve the ribbon equations in 128 attempts.");
    }

    internal int BumpedCount { get; }
    internal uint MemoryUsage => checked((uint)(_data.Length + _thresholds.Length + 16) + (_next?.MemoryUsage ?? 0));

    internal bool Contains(ulong signature)
    {
        if (_data.Length == 0)
            return false;
        ulong hash = StaticFilterHash.Mix(signature + _seed);
        int start = StaticFilterHash.Reduce(hash, _starts);
        if (_thresholds.Length > 0 && start % BucketSize >= _thresholds[start / BucketSize])
            return _next?.Contains(signature) == true;
        UInt128 coefficients = Coefficients(hash);
        byte value = Avx2.IsSupported ? EvaluateVector(start, coefficients) : Evaluate(start, coefficients);
        return value == StaticFilterHash.Fingerprint(hash);
    }

    private byte EvaluateVector(int start, UInt128 coefficients)
    {
        // Expand each coefficient bit into a byte mask, selecting 32 cells at a time.
        ulong low = (ulong)coefficients;
        ulong high = (ulong)(coefficients >> 64);
        Vector256<byte> value = Select(start, (uint)low)
                              ^ Select(start + 32, (uint)(low >> 32))
                              ^ Select(start + 64, (uint)high)
                              ^ Select(start + 96, (uint)(high >> 32));
        Vector128<ulong> folded = value.AsUInt64().GetLower() ^ value.AsUInt64().GetUpper();
        ulong result = folded.GetElement(0) ^ folded.GetElement(1);
        result ^= result >> 32;
        result ^= result >> 16;
        return (byte)(result ^ (result >> 8));
    }

    private Vector256<byte> Select(int start, uint bits)
    {
        Vector256<byte> indices = Vector256.Create(
            Vector128.Create(0UL, 0x0101010101010101UL),
            Vector128.Create(0x0202020202020202UL, 0x0303030303030303UL)).AsByte();
        Vector256<byte> bitMask = Vector256.Create(0x8040201008040201UL).AsByte();
        Vector256<byte> expanded = Avx2.Shuffle(Vector256.Create(bits).AsByte(), indices);
        Vector256<byte> selected = Avx2.CompareEqual(expanded & bitMask, bitMask);
        // Contains only evaluates complete 128-cell bands, including the last start.
        return Vector256.LoadUnsafe(ref MemoryMarshal.GetArrayDataReference(_data), (nuint)start) & selected;
    }

    private byte Evaluate(int start, UInt128 coefficients)
    {
        int value = 0;

        ulong low = (ulong)coefficients;
        ulong high = (ulong)(coefficients >> 64);

        while (low != 0)
        {
            value ^= _data[start + BitOperations.TrailingZeroCount(low)];
            low &= low - 1;
        }

        while (high != 0)
        {
            value ^= _data[start + 64 + BitOperations.TrailingZeroCount(high)];
            high &= high - 1;
        }

        return (byte)value;
    }

    private bool Band(ulong[] signatures, UInt128[] coefficients, byte[] results, List<ulong> bumped, bool useBumping)
    {
        (int Start, ulong Signature, ulong Hash)[] input = new (int, ulong, ulong)[signatures.Length];

        for (int index = 0; index < input.Length; index++)
        {
            ulong hash = StaticFilterHash.Mix(signatures[index] + _seed);
            input[index] = (StaticFilterHash.Reduce(hash, _starts), signatures[index], hash);
        }

        Array.Sort(input, (left, right) => left.Start.CompareTo(right.Start));
        List<(int Pivot, ulong Signature)> group = new List<(int, ulong)>();
        int previousStart = -1;

        foreach ((int start, ulong signature, ulong hash) in input)
        {
            if (start != previousStart)
                group.Clear();
            previousStart = start;

            if (useBumping && start % BucketSize >= _thresholds[start / BucketSize])
            {
                bumped.Add(signature);
                continue;
            }

            int position = start;
            UInt128 row = Coefficients(hash);
            byte value = StaticFilterHash.Fingerprint(hash);

            while (row != 0 && coefficients[position] != 0)
            {
                row ^= coefficients[position];
                value ^= results[position];

                if (row != 0)
                {
                    int shift = (int)UInt128.TrailingZeroCount(row);
                    position += shift;
                    row >>= shift;
                }
            }

            if (row == 0)
            {
                if (!useBumping)
                    return false;
                _thresholds[start / BucketSize] = (byte)(start % BucketSize);

                foreach ((int pivot, ulong previous) in group)
                {
                    coefficients[pivot] = 0;
                    results[pivot] = 0;
                    bumped.Add(previous);
                }

                group.Clear();
                bumped.Add(signature);
            }
            else
            {
                coefficients[position] = row;
                results[position] = value;
                group.Add((position, signature));
            }
        }

        return true;
    }

    private void BackSubstitute(UInt128[] coefficients, byte[] results)
    {
        for (int position = coefficients.Length - 1; position >= 0; position--)
            _data[position] = (byte)(results[position] ^ Evaluate(position, coefficients[position] & ~UInt128.One));
    }

    private static UInt128 Coefficients(ulong hash) => new UInt128(StaticFilterHash.Mix(hash ^ 0xa0761d6478bd642fUL),
        StaticFilterHash.Mix(hash ^ 0xe7037ed1a0b428dbUL) | 1);
}