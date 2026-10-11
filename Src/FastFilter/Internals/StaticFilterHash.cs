namespace Genbox.FastFilter.Internals;

internal static class StaticFilterHash
{
    internal static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xbf58476d1ce4e5b9UL;
        value ^= value >> 27;
        value *= 0x94d049bb133111ebUL;
        return value ^ (value >> 31);
    }

    internal static int Reduce(ulong hash, int size) => (int)Math.BigMul(hash, (ulong)size, out _);
    internal static byte Fingerprint(ulong hash) => (byte)Mix(hash ^ 0xd6e8feb86659fd93UL);

    internal static ulong[] Signatures<T>(ReadOnlySpan<T> keys, Func<T, ulong> hash) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(hash);
        HashSet<ulong> distinct = new HashSet<ulong>(keys.Length);
        foreach (T key in keys)
            distinct.Add(hash(key));
        return distinct.ToArray();
    }
}