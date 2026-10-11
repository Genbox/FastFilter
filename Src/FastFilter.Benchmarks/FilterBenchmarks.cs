using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Benchmarks;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByMethod)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FilterBenchmarks
{
    [ParamsSource(nameof(BuildBloomFilters))]
    public IFilter<long> Filter { get; set; } = null!;

    [Benchmark]
    public void Add() => Filter.Add(824);

    [Benchmark]
    public bool Contains() => Filter.Contains(824);

    public static IEnumerable<IFilter<long>> BuildBloomFilters()
    {
        long[] values = new long[1000];

        for (int i = 0; i < values.Length; i++)
            values[i] = Random.Shared.NextInt64();

        yield return new BloomFilter<long>(values);
        yield return new BlockedBloomFilter<long>(values);
        yield return new BinaryFuse8Filter<long>(values);
        yield return new BinaryFuse16Filter<long>(values);
        yield return new HashSetFilter<long>(values);
    }

    private sealed class HashSetFilter<T>(IEnumerable<T> values) : ImmutableFilter<T> where T : notnull
    {
        private readonly HashSet<T> _set = new HashSet<T>(values);

        public bool Contains(T item) => _set.Contains(item);
        public uint GetMemoryUsage() => throw new NotSupportedException();
    }
}