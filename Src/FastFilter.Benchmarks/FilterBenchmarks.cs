using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Order;
using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Benchmarks;

// A batch of varied queries prevents a single key's branch history from dominating results.
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByMethod)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class FilterBenchmarks
{
    private const int QueryCount = 4096;
    private long[] _values = null!;
    private long[] _present = null!;
    private long[] _absent = null!;
    private ImmutableFilter<long> _filter = null!;

    [Params(1000, 100_000)]
    public int Count { get; set; }

    [ParamsSource(nameof(BuildFilters))]
    public FilterFactory Filter { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        Random random = new Random(42);
        _values = new long[Count];
        for (int i = 0; i < Count; i++)
            _values[i] = random.NextInt64();
        _filter = Build();
        _present = new long[QueryCount];
        _absent = new long[QueryCount];
        for (int i = 0; i < QueryCount; i++)
        {
            _present[i] = _values[random.Next(Count)];
            _absent[i] = ~random.NextInt64(); // Negative keys cannot be in the inserted set.
        }
    }

    [Benchmark(OperationsPerInvoke = QueryCount)]
    public int Present()
    {
        int matches = 0;
        foreach (long key in _present)
            matches += _filter.Contains(key) ? 1 : 0;
        return matches;
    }

    [Benchmark(OperationsPerInvoke = QueryCount)]
    public int Absent()
    {
        int matches = 0;
        foreach (long key in _absent)
            matches += _filter.Contains(key) ? 1 : 0;
        return matches;
    }

    [Benchmark]
    public ImmutableFilter<long> Build() => Filter.Create(_values);

    public static IEnumerable<FilterFactory> BuildFilters()
    {
        yield return new(nameof(BloomFilter<long>), values => new BloomFilter<long>(values, Hash));
        yield return new(nameof(BlockedBloomFilter<long>), values => new BlockedBloomFilter<long>(values, Hash));
        yield return new(nameof(BinaryFuse8Filter<long>), values => new BinaryFuse8Filter<long>(values, Hash));
        yield return new(nameof(BinaryFuse16Filter<long>), values => new BinaryFuse16Filter<long>(values, Hash));
        yield return new(nameof(Xor8Filter<long>), values => new Xor8Filter<long>(values, Hash));
        yield return new(nameof(Ribbon8Filter<long>), values => new Ribbon8Filter<long>(values, Hash));
        yield return new(nameof(Burr8Filter<long>), values => new Burr8Filter<long>(values, Hash));
        yield return new(nameof(Zor8Filter<long>), values => new Zor8Filter<long>(values, Hash));
        yield return new(nameof(VFilter<long>), values => new VFilter<long>(values, Hash));
        yield return new(nameof(HashSetFilter<long>), values => new HashSetFilter<long>(values));
    }

    private static ulong Hash(long key) => StaticFilterHash.Mix(unchecked((ulong)key));

    private sealed class HashSetFilter<T>(IEnumerable<T> values) : ImmutableFilter<T> where T : notnull
    {
        private readonly HashSet<T> _set = new HashSet<T>(values);

        public bool Contains(T item) => _set.Contains(item);
        public uint GetMemoryUsage() => throw new NotSupportedException();
        public override string ToString() => "HashSetFilter";
    }
}

public sealed class FilterFactory(string name, Func<long[], ImmutableFilter<long>> create)
{
    public ImmutableFilter<long> Create(long[] values) => create(values);
    public override string ToString() => name;
}