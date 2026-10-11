# FastFilter

[![NuGet](https://img.shields.io/nuget/v/Genbox.FastFilter.svg?style=flat-square&label=nuget)](https://www.nuget.org/packages/Genbox.FastFilter/)
[![License: MIT AND Apache-2.0](https://img.shields.io/badge/license-MIT%20AND%20Apache--2.0-blue)](THIRD-PARTY-NOTICES.txt)

### Description

A C# library of high-performance approximate membership filters. Test whether a key may belong to a set without storing the original keys.

### Features

Supports the following algorithms:

| Algorithm              | Public type             | Immutable | License                               | Authors                                                                     | Scientific paper                                                                                                                |
|------------------------|-------------------------|:---------:|---------------------------------------|-----------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------|
| Bloom                  | `BloomFilter<T>`        |    No     | [Apache-2.0](LICENSES/Apache-2.0.txt) | Burton H. Bloom                                                             | [Space/time trade-offs in hash coding with allowable errors](https://doi.org/10.1145/362686.362692)                             |
| Register Blocked Bloom | `BlockedBloomFilter<T>` |    No     | [Apache-2.0](LICENSES/Apache-2.0.txt) | Harald Lang, Thomas Neumann, Alfons Kemper, Peter Boncz                     | [Performance-Optimal Filtering: Bloom Overtakes Cuckoo at High Throughput](https://www.vldb.org/pvldb/vol12/p502-lang.pdf)      |
| Binary Fuse, 8-bit     | `BinaryFuse8Filter<T>`  |    Yes    | [Apache-2.0](LICENSES/Apache-2.0.txt) | Thomas Mueller Graf, Daniel Lemire                                          | [Binary Fuse Filters: Fast and Smaller Than Xor Filters](https://arxiv.org/abs/2201.01174)                                      |
| Binary Fuse, 16-bit    | `BinaryFuse16Filter<T>` |    Yes    | [Apache-2.0](LICENSES/Apache-2.0.txt) | Thomas Mueller Graf, Daniel Lemire                                          | [Binary Fuse Filters: Fast and Smaller Than Xor Filters](https://arxiv.org/abs/2201.01174)                                      |
| XOR8                   | `Xor8Filter<T>`         |    Yes    | [Apache-2.0](LICENSES/Apache-2.0.txt) | Thomas Mueller Graf, Daniel Lemire                                          | [Xor Filters: Faster and Smaller Than Bloom and Cuckoo Filters](https://arxiv.org/abs/1912.08258)                               |
| Ribbon8                | `Ribbon8Filter<T>`      |    Yes    | [Apache-2.0](LICENSES/Apache-2.0.txt) | Peter C. Dillinger, Stefan Walzer                                           | [Ribbon filter: practically smaller than Bloom and Xor](https://arxiv.org/abs/2103.02515)                                       |
| BuRR8                  | `Burr8Filter<T>`        |    Yes    | [Apache-2.0](LICENSES/Apache-2.0.txt) | Peter C. Dillinger, Lorenz Hübschle-Schneider, Peter Sanders, Stefan Walzer | [Fast Succinct Retrieval and Approximate Membership using Ribbon](https://arxiv.org/abs/2109.01892)                             |
| ZOR8                   | `Zor8Filter<T>`         |    Yes    | [MIT](LICENSE.txt)                    | Antoine Limasset                                                            | [ZOR filters: fast and smaller than fuse filters](https://arxiv.org/abs/2602.03525)                                             |
| VFilter                | `VFilter<T>`            |    Yes    | [MIT](LICENSES/sux-MIT.txt)           | Sebastiano Vigna                                                            | [ε-Cost Sharding: Scaling Hypergraph-Based Static Functions and Filters to Trillions of Keys](https://arxiv.org/abs/2503.18397) |

Authors refer to the linked papers. Licenses refer to the filter implementations used here, including their underlying adapted code; implementation credits and modifications are recorded in [third-party notices](THIRD-PARTY-NOTICES.txt). ZOR is an independent MIT implementation of the published algorithm.

All filters accept a required `Func<T, ulong> hash`. Construction seeds are managed internally.

Other features:

* Configurable bits per key for Bloom and Blocked Bloom filters.
* Construction from `ReadOnlySpan<T>` for all filters.

### Example usage

Build a Binary Fuse filter from a fixed set of keys:

```csharp
using System;
using Genbox.FastFilter;

int[] data = [1, 42, 30, 481, 58];
BinaryFuse8Filter<int> filter = new BinaryFuse8Filter<int>(data, key => unchecked((ulong)key.GetHashCode()));

Console.WriteLine("Does it contain 42? " + (filter.Contains(42) ? "Maybe" : "No"));
Console.WriteLine("Does it contain 50? " + (filter.Contains(50) ? "Maybe" : "No"));

```

Example output:

```text
Does it contain 42? Maybe
Does it contain 50? No
```

`42` always matches because it was included during construction. `50` can also return `Maybe` due to a false positive.

For a set that grows, create a Bloom filter sized for the expected number of keys:

```csharp
using Genbox.FastFilter;

BloomFilter<int> filter = new BloomFilter<int>(capacity: 100_000, hash: key => unchecked((ulong)key.GetHashCode()), bitsPerKey: 10);
filter.Add(42);
bool maybePresent = filter.Contains(42); // true
```

### FAQ

#### What does an approximate membership filter tell me?

`Contains()` returns `false` when a key is definitely absent, or `true` when it may be present. An inserted key always returns `true`, provided its hash remains unchanged. An absent key can return `true`: this is a false positive.

Use a filter to skip expensive lookups, such as disk or database reads, for keys that are definitely absent. Check positive results against the original data when you need an exact answer.

#### Can I add or remove keys?

Bloom and Blocked Bloom support `Add()` after construction. Their capacity determines the allocated storage; adding more keys than planned increases the false-positive rate.

The other filters are immutable. Supply all keys during construction and rebuild the filter when the set changes. These filters implement `ImmutableFilter<T>` and do not expose `Add()`. None of the filters supports removing individual keys.

#### How do memory usage and false positives relate?

For Bloom and Blocked Bloom, increasing `bitsPerKey` uses more memory to reduce false positives. Binary Fuse offers 8-bit and 16-bit fingerprints, with nominal false-positive probabilities of approximately 0.39% and 0.0015%, respectively, assuming well-distributed hashes.

The research filters use 8-bit fingerprints, with a nominal false-positive probability of approximately 0.39%. Complete ZOR also checks a 16-bit remainder when present, giving approximately 1/256 + 1/65536. Actual rates depend on hashing and workload.

`GetMemoryUsage()` estimates retained filter storage in bytes. It is not a measurement of total managed heap usage or peak construction memory. See the static filter guide for the research filters' accounting details.

#### What are the hashing requirements?

All filters use the supplied `Func<T, ulong>` delegate. It must produce well-distributed 64-bit signatures and remain stable for the filter's lifetime. Keys with the same signature are indistinguishable. Casting `GetHashCode()` to `ulong` does not remove its 32-bit collision limit.

If the hash function needs a seed, capture that seed in the delegate and keep it unchanged. Filters manage layout seeds and construction retries internally using a deterministic sequence. The same signatures, input order, and options reproduce the same layout within the same implementation version. Immutable filters hash the input once and reuse its distinct signatures during construction retries.

### Data structure stats

| Filter             | Estimated memory (bytes) | False positives | Bits per entry | Efficiency ratio |
|--------------------|-------------------------:|----------------:|---------------:|-----------------:|
| BloomFilter        |                1,000,008 |        2.15160% |       8.000064 |           1.4445 |
| BinaryFuse8Filter  |                1,130,504 |        0.39840% |       9.044032 |           1.1345 |
| BinaryFuse16Filter |                2,261,000 |        0.00110% |      18.088000 |           1.0981 |
| BlockedBloomFilter |                1,250,072 |        3.80040% |      10.000576 |           2.1198 |
| Xor8Filter         |                1,230,045 |        0.38070% |       9.840360 |           1.2244 |
| Ribbon8Filter      |                1,100,144 |        0.39170% |       8.801152 |           1.1007 |
| Burr8Filter        |                1,031,644 |        0.39750% |       8.253152 |           1.0349 |
| Zor8Filter         |                1,190,444 |        0.38760% |       9.523552 |           1.1888 |
| VFilter            |                1,280,040 |        0.38930% |      10.240320 |           1.2793 |

Memory is the filter's `GetMemoryUsage()` estimate. The **efficiency ratio** compares the actual bits per entry with the theoretical minimum needed for the observed false-positive rate:

```text
Bits per entry = estimated memory in bytes * 8 / number of inserted keys
Efficiency ratio = bits per entry / log2(1 / p)
Space overhead (%) = (efficiency ratio - 1) * 100
```

Here, `p` is the false-positive rate expressed as a fraction, so `0.39%` becomes `0.0039`. For example, a rate of `1/256` (approximately `0.3906%`) requires a theoretical minimum of 8 bits per entry. A filter using 9 bits per entry has a ratio of `9 / 8 = 1.125`, or **12.5% space overhead** above that minimum.

For example, the `Burr8Filter` ratio of `1.0349` in the table means approximately **3.49% extra space** above the minimum for its observed false-positive rate. It does not mean 3.49% false positives.

A ratio of `1.0` corresponds to the theoretical minimum, `1.1` means 10% extra space, and `2.0` means twice the minimum space. **Lower is better for space efficiency.** The ratio does not measure lookup or construction speed. Because the example uses estimated memory and a sampled false-positive rate, its reported ratios are approximate and vary between runs.

### Benchmarks

|    Keys | Filter             | Present (ns) | Absent (ns) | Construction (ms) | Construction allocated (MiB) |
| ------: | ------------------ | -----------: | ----------: | ----------------: | ---------------------------: |
|   1,000 | BloomFilter        |         5.73 |        7.03 |            0.0061 |                       0.0010 |
|   1,000 | BlockedBloomFilter |         3.20 |        3.21 |            0.0026 |                       0.0013 |
|   1,000 | BinaryFuse8Filter  |         3.96 |        4.08 |            0.0242 |                       0.0565 |
|   1,000 | BinaryFuse16Filter |         3.92 |        3.89 |            0.0239 |                       0.0578 |
|   1,000 | Xor8Filter         |         4.83 |        4.82 |            0.0169 |                       0.0647 |
|   1,000 | Ribbon8Filter      |        10.22 |       10.07 |            0.0865 |                       0.0732 |
|   1,000 | Burr8Filter        |        11.05 |       10.99 |            0.0908 |                       0.0712 |
|   1,000 | Zor8Filter         |         6.25 |       10.18 |            0.0671 |                       0.1609 |
|   1,000 | VFilter            |         8.27 |        8.92 |            0.0681 |                       0.2825 |
|   1,000 | HashSetFilter      |         2.48 |        2.18 |            0.0030 |                       0.0212 |
| 100,000 | BloomFilter        |         6.41 |        8.59 |            0.7262 |                       0.0954 |
| 100,000 | BlockedBloomFilter |         3.44 |        3.48 |            0.3289 |                       0.1194 |
| 100,000 | BinaryFuse8Filter  |         4.32 |        4.49 |            3.9573 |                       5.2801 |
| 100,000 | BinaryFuse16Filter |         4.29 |        4.30 |            3.9177 |                       5.3934 |
| 100,000 | Xor8Filter         |         5.04 |        5.07 |            5.1850 |                       6.3560 |
| 100,000 | Ribbon8Filter      |        10.29 |       10.26 |           14.2290 |                       7.0202 |
| 100,000 | Burr8Filter        |        11.30 |       11.27 |           25.5896 |                       6.9446 |
| 100,000 | Zor8Filter         |         8.27 |       10.07 |           13.8747 |                      17.8025 |
| 100,000 | VFilter            |         8.80 |        8.70 |            7.1972 |                      15.0309 |
| 100,000 | HashSetFilter      |         4.34 |        3.89 |            1.0603 |                       2.0722 |

### License

FastFilter combines [MIT-licensed original code](LICENSE.txt) with Apache-2.0 and MIT third-party adaptations. The package license expression is `MIT AND Apache-2.0`. See [third-party notices](THIRD-PARTY-NOTICES.txt) for component attribution and [LICENSES](LICENSES) for the bundled upstream license texts.