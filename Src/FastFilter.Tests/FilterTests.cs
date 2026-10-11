using Genbox.FastFilter.Abstracts;
using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Tests;

public class FilterTests
{
    [Theory]
    [MemberData(nameof(GenerateTests), DisableDiscoveryEnumeration = true)]
    public void ContainsInsertedKeys(ImmutableFilter<ulong> filter, ulong[] keys)
    {
        foreach (ulong key in keys)
            Assert.True(filter.Contains(key));
    }

    public static IEnumerable<TheoryDataRow<ImmutableFilter<ulong>, ulong[]>> GenerateTests()
    {
        int[] sizes = [1, 1000, 10000, 100000, 1000000];

        foreach (int size in sizes)
        {
            ulong[] keys = CreateKeys(size);
            foreach (ImmutableFilter<ulong> filter in BuildFilters(keys))
                yield return new TheoryDataRow<ImmutableFilter<ulong>, ulong[]>(filter, keys);
        }
    }

    [Theory]
    [MemberData(nameof(SeededFilterCases), DisableDiscoveryEnumeration = true)]
    public void ContainsSparse64BitMembersAndRejectsNonmembers(ImmutableFilter<ulong> filter, ulong[] keys)
    {
        foreach (ulong key in keys)
            Assert.True(filter.Contains(key));
        int falsePositives = 0;

        for (ulong index = 16384; index < 81920; index++)
        {
            if (filter.Contains((index << 32) | index))
                falsePositives++;
        }

        (int minimum, int maximum) = filter switch
        {
            BinaryFuse16Filter<ulong> => (0, 20),
            BloomFilter<ulong> or BlockedBloomFilter<ulong> => (100, 4000),
            _ => (100, 600)
        };
        Assert.InRange(falsePositives, minimum, maximum);
    }

    [Theory]
    [MemberData(nameof(EmptyAndRepeatedFilterCases), DisableDiscoveryEnumeration = true)]
    public void RepresentsEmptySingletonAndRepeatedKeys(ImmutableFilter<ulong> empty, ImmutableFilter<ulong> singleton)
    {
        Assert.False(empty.Contains(41));
        Assert.True(singleton.Contains(41));
        Assert.True(singleton.GetMemoryUsage() > 0);
    }

    public static IEnumerable<TheoryDataRow<ImmutableFilter<ulong>, ImmutableFilter<ulong>>> EmptyAndRepeatedFilterCases()
    {
        foreach ((ImmutableFilter<ulong> empty, ImmutableFilter<ulong> singleton) in BuildEmptyCapableFilters([]).Zip(BuildEmptyCapableFilters([41, 41, 41])))
            yield return new TheoryDataRow<ImmutableFilter<ulong>, ImmutableFilter<ulong>>(empty, singleton);
    }

    public static IEnumerable<TheoryDataRow<ImmutableFilter<ulong>, ulong[]>> SeededFilterCases()
    {
        ulong[] keys = new ulong[16384];
        for (int index = 0; index < keys.Length; index++)
            keys[index] = ((ulong)index << 32) | (uint)index;

        ulong[] seeds = [1, 917];

        foreach (ulong seed in seeds)
        {
            foreach (ImmutableFilter<ulong> filter in BuildFilters(keys, key => StaticFilterHash.Mix(unchecked(key + seed))))
                yield return new TheoryDataRow<ImmutableFilter<ulong>, ulong[]>(filter, keys);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(917UL)]
    public void ConstructionReproducesMembershipWithCapturedHashSeed(ulong hashSeed)
    {
        Func<ulong, ulong> hash = key => StaticFilterHash.Mix(unchecked(key + hashSeed));
        ulong[] keys = CreateKeys(4096);

        foreach ((ImmutableFilter<ulong> first, ImmutableFilter<ulong> second) in
                 BuildFilters(keys, hash).Zip(BuildFilters(keys, hash)))
        {
            Assert.Equal(first.GetMemoryUsage(), second.GetMemoryUsage());
            for (ulong key = 4096; key < 12288; key++)
                Assert.Equal(first.Contains(key), second.Contains(key));
        }
    }

    private static ulong[] CreateKeys(int count)
    {
        ulong[] keys = new ulong[count];
        for (int index = 0; index < keys.Length; index++)
            keys[index] = (ulong)index;
        return keys;
    }

    private static IEnumerable<ImmutableFilter<ulong>> BuildFilters(ulong[] keys, Func<ulong, ulong>? hash = null)
    {
        hash ??= Hash;
        yield return new BloomFilter<ulong>(keys, hash);
        yield return new BlockedBloomFilter<ulong>(keys, hash);
        yield return new BinaryFuse8Filter<ulong>(keys, hash);
        yield return new BinaryFuse16Filter<ulong>(keys, hash);

        foreach (ImmutableFilter<ulong> filter in BuildEmptyCapableFilters(keys, hash))
            yield return filter;
    }

    private static IEnumerable<ImmutableFilter<ulong>> BuildEmptyCapableFilters(ulong[] keys, Func<ulong, ulong>? hash = null)
    {
        hash ??= Hash;
        yield return new Xor8Filter<ulong>(keys, hash);
        yield return new Ribbon8Filter<ulong>(keys, hash);
        yield return new Burr8Filter<ulong>(keys, hash);
        yield return new Zor8Filter<ulong>(keys, hash);
        yield return new VFilter<ulong>(keys, hash);
    }

    private static ulong Hash(ulong key) => StaticFilterHash.Mix(key);
}