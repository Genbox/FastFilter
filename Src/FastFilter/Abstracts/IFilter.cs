namespace Genbox.FastFilter.Abstracts;

/// <summary>Provides probabilistic set membership queries and supports adding keys.</summary>
/// <typeparam name="T">The type of keys stored in the filter.</typeparam>
public interface IFilter<in T> : ImmutableFilter<T> where T : notnull
{
    /// <summary>Adds a key to the filter.</summary>
    /// <param name="key">The key to add.</param>
    void Add(T key);
}