using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Examples;

internal static class Program
{
    private static void Main()
    {
        const int size = 1_000_000;
        const int trials = 1_000_000;

        int[] data = Enumerable.Range(0, size).ToArray();

        BloomFilter<int> bf = new BloomFilter<int>(data);
        Console.WriteLine("# BloomFilter");
        Console.WriteLine($"Memory: {bf.GetMemoryUsage():N0} bytes");
        Console.WriteLine($"False positives: {ComputeFalsePositiveRate(bf, data.Length, trials):P5}");
        Console.WriteLine();

        BinaryFuse8Filter<int> fuse8 = new BinaryFuse8Filter<int>(data);
        Console.WriteLine("# BinaryFuse8Filter");
        Console.WriteLine($"Memory: {fuse8.GetMemoryUsage():N0} bytes");
        Console.WriteLine($"False positives: {ComputeFalsePositiveRate(fuse8, data.Length, trials):P5}");
        Console.WriteLine();

        BinaryFuse16Filter<int> fuse16 = new BinaryFuse16Filter<int>(data);
        Console.WriteLine("# BinaryFuse16Filter");
        Console.WriteLine($"Memory: {fuse16.GetMemoryUsage():N0} bytes");
        Console.WriteLine($"False positives: {ComputeFalsePositiveRate(fuse16, data.Length, trials):P5}");
        Console.WriteLine();

        BlockedBloomFilter<int> blocked = new BlockedBloomFilter<int>(data);
        Console.WriteLine("# BlockedBloomFilter");
        Console.WriteLine($"Memory: {blocked.GetMemoryUsage():N0} bytes");
        Console.WriteLine($"False positives: {ComputeFalsePositiveRate(blocked, data.Length, trials):P5}");
    }

    private static double ComputeFalsePositiveRate(IBloomFilter<int> filter, int size, int trials)
    {
        int falsePositives = 0;

        for (int i = 0; i < trials; i++)
        {
            int randomKey = Random.Shared.Next();

            if (!filter.Contains(randomKey))
                continue;

            if (randomKey >= size)
                falsePositives++;
        }

        return falsePositives / (double)trials;
    }
}