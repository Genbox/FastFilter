namespace Genbox.FastFilter.Abstracts;

public interface IBloomFilter<in T> where T : notnull
{
    void Add(T key);
    bool Contains(T key);
    uint GetMemoryUsage();
}