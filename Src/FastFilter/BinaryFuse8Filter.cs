namespace Genbox.FastFilter;

/// <summary>
/// 8-bit variant of the binary fuse filter. Uses 9 bits per entry. False-positive rate is ~0.39%.
/// </summary>
/// <param name="data">Read-only data to add to the filter. Repeated hash signatures are stored once.</param>
/// <param name="hash">A well-distributed 64-bit hash function that remains stable for the filter's lifetime.</param>
public class BinaryFuse8Filter<T>(ReadOnlySpan<T> data, Func<T, ulong> hash) : BinaryFuseFilter<T, byte>(data, hash) where T : notnull;