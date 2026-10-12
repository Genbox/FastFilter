using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Examples;

[SuppressMessage("Major Vulnerability", "S2245:Pseudorandom number generators (PRNGs) should not be used in security contexts")]
internal static class Program
{
    private static void Main()
    {
        const int size = 1_000_000;
        const int trials = 1_000_000;

        int[] data = Enumerable.Range(0, size).ToArray();
        int[] queries = new int[trials];
        Random random = new Random(42);

        // Stored keys are [0, size); every query is absent and shared by all filters.
        for (int i = 0; i < queries.Length; i++)
            queries[i] = random.Next(size, int.MaxValue);

        Console.WriteLine($"{size:N0} keys, {trials:N0} absent-key queries (random seed: 42)");
        Console.WriteLine();
        Console.WriteLine($"{"Filter",-20} {"Memory (bytes)",15} {"False positives",16} {"Bits/key",10} {"Efficiency",12} {"ns/query (approx)",12}");
        Console.WriteLine(new string('-', 90));

        PrintStatistics(new BloomFilter<int>(data, Hash), size, queries);
        PrintStatistics(new BinaryFuse8Filter<int>(data, Hash), size, queries);
        PrintStatistics(new BinaryFuse16Filter<int>(data, Hash), size, queries);
        PrintStatistics(new BlockedBloomFilter<int>(data, Hash), size, queries);
        PrintStatistics(new Xor8Filter<int>(data, Hash), size, queries);
        PrintStatistics(new Ribbon8Filter<int>(data, Hash), size, queries);
        PrintStatistics(new Burr8Filter<int>(data, Hash), size, queries);
        PrintStatistics(new Zor8Filter<int>(data, Hash), size, queries);
        PrintStatistics(new VFilter<int>(data, Hash), size, queries);

        Console.WriteLine();
        Console.WriteLine("Memory estimates retained storage; efficiency is bits/key divided by -log2(false-positive rate).");
    }

    // A non-cryptographic 64-bit mixer for integer keys.
    private static ulong Hash(int key)
    {
        unchecked
        {
            ulong value = (ulong)key;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            return value ^ (value >> 31);
        }
    }

    private static void PrintStatistics(ImmutableFilter<int> filter, int size, ReadOnlySpan<int> queries)
    {
        uint memory = filter.GetMemoryUsage();
        const int measuredPasses = 5;
        for (int pass = 0; pass < 3; pass++)
            CountMatches(filter, queries);

        long matches = 0;
        long start = Stopwatch.GetTimestamp();

        for (int pass = 0; pass < measuredPasses; pass++)
            matches += CountMatches(filter, queries);

        double elapsedNanoseconds = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
        double queryCount = (double)queries.Length * measuredPasses;
        double nanosecondsPerQuery = elapsedNanoseconds / queryCount;
        double falsePositiveRate = matches / queryCount;
        double bitsPerEntry = (memory * 8.0) / size;
        double efficiency = bitsPerEntry / -Math.Log2(falsePositiveRate);

        Console.WriteLine($"{filter,-20} {memory,15:N0} {falsePositiveRate,16:P4} {bitsPerEntry,10:F3} {efficiency,12:F3} {nanosecondsPerQuery,12:F2}");
    }

    private static int CountMatches(ImmutableFilter<int> filter, ReadOnlySpan<int> queries)
    {
        int falsePositives = 0;

        foreach (int key in queries)
        {
            if (filter.Contains(key))
                falsePositives++;
        }

        return falsePositives;
    }
}