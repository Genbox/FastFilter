using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Tests;

public class BloomFilterTests
{
    [Fact]
    public void BloomFilterRejectsSizesThatOverflowBackingArrayLength() => Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter<int>(int.MaxValue, uint.MaxValue));

    [Fact]
    public void BloomFilterRejectsBitsPerKeyThatOverflowProbeCount() => Assert.Throws<ArgumentOutOfRangeException>(() => new BloomFilter<int>(1, 369));

    [Fact]
    public void BlockedBloomFilterRejectsSizesThatOverflowBackingArrayLength() => Assert.Throws<ArgumentOutOfRangeException>(() => new BlockedBloomFilter<int>(int.MaxValue, int.MaxValue));

    [Theory]
    [MemberData(nameof(GenerateTests))]
    public void ContainsInsertedKeys(IBloomFilter<long> filter, long[] values)
    {
        foreach (long value in values)
            Assert.True(filter.Contains(value));
    }

    public static TheoryData<IBloomFilter<long>, long[]> GenerateTests()
    {
        TheoryData<IBloomFilter<long>, long[]> data = new TheoryData<IBloomFilter<long>, long[]>();

        //1 is to check special cases
        uint[] sizes = [1, 1000, 10000, 100000, 1000000];

        foreach (uint size in sizes)
        {
            long[] values = new long[size];

            for (long i = 0; i < size; i++)
                values[i] = i;

            data.Add(new BinaryFuse8Filter<long>(values), values);
            data.Add(new BinaryFuse16Filter<long>(values), values);
            data.Add(new BloomFilter<long>(values), values);
            data.Add(new BlockedBloomFilter<long>(values), values);
        }

        return data;
    }
}