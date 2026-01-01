using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Tests;

public class BloomFilterTests(ITestOutputHelper output)
{
    [Theory]
    [MemberData(nameof(GenerateTests))]
    public void GenericTest(IBloomFilter<long> filter, long[] values, uint size)
    {
        for (uint i = 0; i < size; i++)
            Assert.True(filter.Contains(values[i]));

        uint randomMatches = 0;
        const uint trials = 10_000_000;

        for (uint i = 0; i < trials; i++)
        {
            long randomKey = Random.Shared.NextInt64();

            if (!filter.Contains(randomKey))
                continue;

            if (randomKey >= size)
                randomMatches++;
        }

        double fpp = randomMatches / (double)trials;
        output.WriteLine($"False positive: {fpp * 100:N5}%");
        uint mem = filter.GetMemoryUsage();
        output.WriteLine($"Total memory usage: {mem:N0} bytes");
        double bpe = mem * 8.0 / size;
        output.WriteLine($"Bits per entry: {bpe}");
        output.WriteLine($"Efficiency ratio: {bpe / (-Math.Log(fpp) / Math.Log(2))}");
    }

    public static TheoryData<IBloomFilter<long>, long[], uint> GenerateTests()
    {
        TheoryData<IBloomFilter<long>, long[], uint> data = new TheoryData<IBloomFilter<long>, long[], uint>();

        //1 is to check special cases
        uint[] sizes = [1, 1000, 10000, 100000, 1000000];

        foreach (uint size in sizes)
        {
            long[] values = new long[size];

            for (long i = 0; i < size; i++)
                values[i] = i;

            data.Add(new BinaryFuse8Filter<long>(values), values, size);
            data.Add(new BinaryFuse16Filter<long>(values), values, size);
            data.Add(new BloomFilter<long>(values), values, size);
            data.Add(new BlockedBloomFilter<long>(values), values, size);
        }

        return data;
    }
}