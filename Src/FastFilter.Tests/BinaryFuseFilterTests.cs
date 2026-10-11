using Genbox.FastFilter.Internals;

namespace Genbox.FastFilter.Tests;

public class BinaryFuseFilterTests
{
    [Fact]
    public void ConstructorHandlesDistinctKeysWithSameHashCode()
    {
        CollidingKey first = new CollidingKey(1);
        CollidingKey second = new CollidingKey(2);

        BinaryFuse8Filter<CollidingKey> filter = new BinaryFuse8Filter<CollidingKey>([first, second, first], key => StaticFilterHash.Mix((ulong)key.Value));

        Assert.True(filter.Contains(first));
        Assert.True(filter.Contains(second));
    }

    private readonly record struct CollidingKey(int Value)
    {
        public override int GetHashCode() => 0;
    }
}