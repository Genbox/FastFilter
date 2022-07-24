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