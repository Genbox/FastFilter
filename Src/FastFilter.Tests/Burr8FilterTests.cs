using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Tests;

public class Burr8FilterTests
{
    [Fact]
    public void BurrRoutesOverflowKeysThroughSubsequentRibbons()
    {
        ulong[] keys = new ulong[32768];
        for (int index = 0; index < keys.Length; index++)
            keys[index] = (ulong)index;

        Burr8Filter<ulong> filter = new Burr8Filter<ulong>(keys, Hash);
        Assert.True(filter.BumpedCount > 0);
        foreach (ulong key in keys)
            Assert.True(filter.Contains(key));
    }

    private static ulong Hash(ulong key) => StaticFilterHash.Mix(key);
}