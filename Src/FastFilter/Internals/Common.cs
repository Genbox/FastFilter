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
// Adapted from FastFilter/fastfilter_cpp hashing and range-reduction helpers.
// Modified: C# helpers with managed seed generation.

using System.Runtime.CompilerServices;

namespace Genbox.FastFilter.Internals;

internal static class Common
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Murmur64(ulong h)
    {
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccd;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53;
        h ^= h >> 33;
        return h;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong MixSplit(int key, ulong seed) => Murmur64((ulong)key + seed);

    // http://lemire.me/blog/2016/06/27/a-fast-alternative-to-the-modulo-reduction/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint FastRange32(uint word, uint p) => (uint)(((ulong)word * p) >> 32);

    internal static ulong GetSeed()
    {
        ulong seed = (ulong)Random.Shared.NextInt64();
        seed <<= 32;
        seed |= (ulong)Random.Shared.NextInt64();
        return seed;
    }
}