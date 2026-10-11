using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Tests;

public class VFuncCoreTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(32)]
    [InlineData(64)]
    public void ShardedRetrievalSolvesValuesAndRestoresPackedState(int bits)
    {
        ulong[] keys = new ulong[12000];
        ulong[] values = new ulong[keys.Length];
        ulong mask = bits == 64 ? ulong.MaxValue : (1UL << bits) - 1;

        for (int index = 0; index < keys.Length; index++)
        {
            keys[index] = (ulong)index;
            values[index] = Hash((ulong)index, 48) & mask;
        }

        VFuncCore core = new VFuncCore(keys, values, bits, 29);
        VFuncCore restored = new VFuncCore(core.Count, core.BitWidth, core.Seed, core.SegmentLength, core.SegmentCount, core.ShardCount, (ulong[])core.Data.Clone(), (ulong[])core.ShardSeeds.Clone());
        Assert.True(core.ShardCount > 1);
        Assert.True(core.GaussianEquationCount > 0);
        for (int index = 0; index < keys.Length; index++)
            Assert.Equal(values[index], restored.Get(keys[index]));
    }

    private static ulong Hash(ulong key, ulong seed) => StaticFilterHash.Mix(unchecked(key + seed));
}