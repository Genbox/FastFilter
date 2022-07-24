using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Genbox.FastFilter.Abstracts;

namespace Genbox.FastFilter.Benchmarks;

[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByMethod)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class BloomFilterBenchmarks
{
    [Benchmark]
    public void Add() => Filter.Add(824);

    [Benchmark]
    public bool Contains() => Filter.Contains(824);

    [ParamsSource(nameof(BuildBloomFilters))]
    public IBloomFilter<long> Filter { get; set; }

    public IEnumerable<IBloomFilter<long>> BuildBloomFilters()
    {
        long[] values = new long[1000];

        for (int i = 0; i < values.Length; i++)
            values[i] = Random.Shared.NextInt64();

        yield return new BloomFilter<long>(values);
        yield return new BlockedBloomFilter<long>(values);
        yield return new BinaryFuse8Filter<long>(values);
        yield return new BinaryFuse16Filter<long>(values);
        yield return new HashSetBloomFilter<long>(values);
    }

    private class HashSetBloomFilter<T>(IEnumerable<T> values) : IBloomFilter<T> where T : notnull
    {
        private readonly HashSet<T> _set = new HashSet<T>(values);

        public void Add(T item) => _set.Add(item);
        public bool Contains(T item) => _set.Contains(item);
        public uint GetMemoryUsage() => throw new NotImplementedException();
    }
}