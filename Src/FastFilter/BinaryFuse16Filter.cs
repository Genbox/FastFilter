namespace Genbox.FastFilter;

/// <summary>
/// 16-bit variant of the binary fuse filter. Uses 18 bits per entry. False-positive rate is ~0.0015%.
/// </summary>
/// <param name="data">Read-only data to add to the filter. Try to avoid duplicates in the data.</param>
public class BinaryFuse16Filter<T>(ReadOnlySpan<T> data) : BinaryFuseFilter<T, ushort>(data) where T : notnull;