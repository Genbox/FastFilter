using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Tests;

public class Zor8FilterTests
{
    [Fact]
    public void ZorRemainderPreservesAbandonedMembers()
    {
        ulong[] keys = new ulong[65536];
        for (int index = 0; index < keys.Length; index++)
            keys[index] = (ulong)index;

        Zor8Filter<ulong> filter = new Zor8Filter<ulong>(keys, Hash);
        Assert.True(filter.RemainderCount > 0);
        foreach (ulong key in keys)
            Assert.True(filter.Contains(key));
    }

    private static ulong Hash(ulong key) => StaticFilterHash.Mix(key);
}