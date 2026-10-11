namespace Genbox.FastFilter.Abstracts;

/// <summary>Provides probabilistic set membership queries.</summary>
/// <typeparam name="T">The type of keys stored in the filter.</typeparam>
public interface IBloomFilter<in T> where T : notnull
{
    /// <summary>Adds a key to a mutable filter.</summary>
    /// <param name="key">The key to add.</param>
    void Add(T key);

    /// <summary>Returns whether the key may be present. A false result means it is absent.</summary>
    /// <param name="key">The key to look up.</param>
    bool Contains(T key);

    /// <summary>Returns the estimated memory usage in bytes.</summary>
    uint GetMemoryUsage();
}