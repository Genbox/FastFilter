using System.Diagnostics.CodeAnalysis;

namespace Genbox.FastFilter.Abstracts;

/// <summary>Provides probabilistic set membership queries.</summary>
/// <typeparam name="T">The type of keys stored in the filter.</typeparam>
[SuppressMessage("Naming", "CA1715:Identifiers should have correct prefix", Justification = "The public interface is named ImmutableFilter by design.")]
public interface ImmutableFilter<in T> where T : notnull
{
    /// <summary>Returns whether the key may be present. A false result means it is absent.</summary>
    /// <param name="key">The key to look up.</param>
    bool Contains(T key);

    /// <summary>Returns the estimated memory usage in bytes.</summary>
    uint GetMemoryUsage();
}