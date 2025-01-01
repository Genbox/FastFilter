namespace Genbox.FastFilter;

/// <summary>
/// 8-bit variant of the binary fuse filter. Uses 9 bits per entry. False-positive rate is ~0.39%.
/// </summary>
/// <param name="data">Read-only data to add to the filter. Try to avoid duplicates in the data.</param>
public class BinaryFuse8Filter<T>(ReadOnlySpan<T> data) : BinaryFuseFilter<T, byte>(data) where T : notnull;