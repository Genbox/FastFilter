using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Examples;

internal static class Program
{
    private static void Main()
    {
        const int size = 1_000_000;
        const int trials = 1_000_000;

        int[] data = Enumerable.Range(0, size).ToArray();

        PrintStatistics(new BloomFilter<int>(data, Hash), size, trials);
        PrintStatistics(new BinaryFuse8Filter<int>(data, Hash), size, trials);
        PrintStatistics(new BinaryFuse16Filter<int>(data, Hash), size, trials);
        PrintStatistics(new BlockedBloomFilter<int>(data, Hash), size, trials);
        PrintStatistics(new Xor8Filter<int>(data, Hash), size, trials);
        PrintStatistics(new Ribbon8Filter<int>(data, Hash), size, trials);
        PrintStatistics(new Burr8Filter<int>(data, Hash), size, trials);
        PrintStatistics(new Zor8Filter<int>(data, Hash), size, trials);
        PrintStatistics(new VFilter<int>(data, Hash), size, trials);
    }

    private static ulong Hash(int key) => unchecked((ulong)key.GetHashCode());

    private static void PrintStatistics(ImmutableFilter<int> filter, int size, int trials)
    {
        uint memory = filter.GetMemoryUsage();
        double falsePositiveRate = ComputeFalsePositiveRate(filter, size, trials);
        double bitsPerEntry = (memory * 8.0) / size;

        Console.WriteLine($"# {filter}");
        Console.WriteLine($"Memory: {memory:N0} bytes");
        Console.WriteLine($"False positives: {falsePositiveRate:P5}");
        Console.WriteLine($"Bits per entry: {bitsPerEntry}");
        Console.WriteLine($"Efficiency ratio: {bitsPerEntry / (-Math.Log(falsePositiveRate) / Math.Log(2))}");
        Console.WriteLine();
    }

    private static double ComputeFalsePositiveRate(ImmutableFilter<int> filter, int size, int trials)
    {
        int falsePositives = 0;

        for (int i = 0; i < trials; i++)
        {
            int randomKey = Random.Shared.Next();

            if (filter.Contains(randomKey) && randomKey >= size)
                falsePositives++;
        }

        return falsePositives / (double)trials;
    }
}