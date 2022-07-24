# FastFilter

[![NuGet](https://img.shields.io/nuget/v/Genbox.FastFilter.svg?style=flat-square&label=nuget)](https://www.nuget.org/packages/Genbox.FastFilter/)
[![License](https://img.shields.io/github/license/Genbox/FastFilter)](https://github.com/Genbox/FastFilter/blob/main/LICENSE.txt)

### Description

FastFilter is a C# implementation of a few different Bloom filters with focus on high performance. It contains the following Bloom filter implementations:

- Bloom filter
- Register Blocked Bloom filter
- BinaryFuse filter

### Features

* Add()
* Contains()

### Example

```csharp
static void Main()
{
    int[] data = [1, 42, 30, 481, 58];
    BinaryFuse8Filter<int> fuse = new BinaryFuse8Filter<int>(data);

    Console.WriteLine("Does it contain 42? " + (fuse.Contains(42) ? "Maybe" : "No"));
    Console.WriteLine("Does it contain 50? " + (fuse.Contains(50) ? "Maybe" : "No"));
}
```

Output:

```
Does it contain 42? Maybe
Does it contain 50? No
```

The reason it says "Maybe" is because Bloom filters can have false positives, but never false negatives. If it says "No", it is guaranteed that the item is not in the filter.

### Benchmarks

| Method   | Filter             |     Mean |     Error |    StdDev |
|----------|--------------------|---------:|----------:|----------:|
| Add      | BinaryFuse16Filter |       NA |        NA |        NA |
| Add      | BinaryFuse8Filter  |       NA |        NA |        NA |
| Add      | BlockedBloomFilter | 1.017 ns | 0.0442 ns | 0.0196 ns |
| Add      | HashSetBloomFilter | 3.262 ns | 0.0845 ns | 0.0559 ns |
| Add      | BloomFilter        | 4.689 ns | 0.1011 ns | 0.0156 ns |
|          |                    |          |           |           |
| Contains | BlockedBloomFilter | 1.755 ns | 0.0459 ns | 0.0119 ns |
| Contains | BinaryFuse16Filter | 2.274 ns | 0.0661 ns | 0.0294 ns |
| Contains | HashSetBloomFilter | 2.575 ns | 0.0372 ns | 0.0058 ns |
| Contains | BinaryFuse8Filter  | 2.632 ns | 0.0690 ns | 0.0107 ns |
| Contains | BloomFilter        | 4.531 ns | 0.0994 ns | 0.0441 ns |

HashSetBloomFilter is a Bloom filter that uses a HashSet as a backing store. It is only included for comparison, since .NET does not natively have a Bloom filter.
BinaryFuse filters give NA for `Add()` because they are immutable once constructed.

For retaining 1000 64bit integers (8000 bytes), the memory usage is as follows:

| Filter             |      Memory | False-positives |
|--------------------|------------:|:----------------|
| BloomFilter        |  1008 bytes | 2.144%          |
| BlockedBloomFilter |  1320 bytes | 3.432%          |
| BinaryFuse8Filter  |  1416 bytes | 0.387%          |
| BinaryFuse16Filter |  2824 bytes | 0.001%          |
| HashSetBloomFilter | 30920 bytes | 0%              |

It is possible to tweak the false positive percentage for BloomFilter and BlockedBloomFilter. Lower false positive percent means more memory usage.