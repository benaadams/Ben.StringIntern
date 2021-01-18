# Ben.StringIntern

[![NuGet version (Ben.StringIntern)](https://img.shields.io/nuget/v/Ben.StringIntern.svg?style=flat-square)](https://www.nuget.org/packages/Ben.StringIntern/)
![.NET 5.0](https://github.com/benaadams/Ben.StringIntern/workflows/.NET%205.0/badge.svg)
![.NET Core 3.1](https://github.com/benaadams/Ben.StringIntern/workflows/.NET%20Core%203.1/badge.svg)

Inspired by this issue being closed: "API request: string.Intern(ReadOnlySpan<char> ...)" [#28368](https://github.com/dotnet/runtime/issues/28368)

Shared pool is capped; with 2 generation LRU eviction and further evictions on Gen2 GC collections.

## Example usuage

Collections
```csharp
using Ben.Collections;

array = array.ToInternedArray();
list = list.ToInternedList();
dict = dict.ToInternedDictionary();
string val = stringBuilder.Intern();

var conDict = dict.ToInternedConcurrentDictionary();
```

Sql query
```csharp
using static Ben.Collections.Specialized.StringCache;

while (await reader.ReadAsync())
{
    var id = reader.GetInt32(0);
    
    // Dedupe the string before you keep it
    var category = Intern(reader.GetString(1));
    
    // ...
}
```

## API

```csharp
namespace Ben.Collections.Specialized
{
    public class InternPool
    {
        // Thread-safe shared intern pool; bounded at some capacity, with some max length
        public static SharedInternPool Shared { get; }
        // Unbounded
        public InternPool();
        // Unbounded with prereserved capacity
        public InternPool(int capacity);
        // Capped size with prereserved capacity, entires evicted based on 2 generation LRU
        public InternPool(int capacity, int maxCount);
        // Capped size; max pooled string length, with prereserved capacity, entires evicted based on 2 generation LRU
        public InternPool(int capacity, int maxCount, int maxLength)
        
        // Deduplicated; unbounded
        public InternPool(IEnumerable<string> collection);
        // Deduplicated; capped size, entires evicted based on 2 generation LRU
        public InternPool(IEnumerable<string> collection, int maxCount);
    
        [return: NotNullIfNotNull("value")]
        public string? Intern(string? value);
        public string Intern(ReadOnlySpan<char> value);
        public string InternAscii(ReadOnlySpan<byte> asciiValue);
        public string InternUtf8(ReadOnlySpan<byte> utf8Value);
        
        // Gets the number of strings currently in the pool.
        public int Count { get; }
        // Count of strings checked
        public long Considered { get; }
        // Count of strings deduplicated
        public long Deduped { get; }
        // Count of strings added to the pool, may be larger than Count if there is a maxCount.
        public long Added { get; }
    }
}
```

## Stats for `Shared` pool

Using the dotnet counters as `InternPool`

Get the proc id
```
> dotnet-counters ps

     14600 MyApp MyAppPath\MyApp.exe
```
Then query monitor that process
```
> dotnet-counters monitor InternPool --process-id 14600

Press p to pause, r to resume, q to quit.
    Status: Running

[InternPool]
    Considered (Count / 1 sec)                     4,497
    Deduped (Count / 1 sec)                        4,496
    Evicted (Count / 1 sec)                            0
    Total Considered                           1,357,121
    Total Count                                    7,811
    Total Deduped                              1,316,376
    Total Evicted                                 32,934
    Total Gen2 Sweeps                                  3
```

## Building

`dotnet build -c Release`

## Performance
```
cd tests/Benchmarks
dotnet run -c Release
```

|                     Method |         Dataset |     Mean |   Error | Ratio |  Allocated |
|--------------------------- |---------------- |---------:|--------:|------:|-----------:|
|                    Default | 2M (20k-Values) | 287.1 ms | 6.73 ms |  1.00 | 63991944 B |
| MTHP_StringPool_Stackalloc | 2M (20k-Values) | 829.1 ms | 8.20 ms |  2.89 | 63991944 B |
|    MTHP_StringPool_Encoder | 2M (20k-Values) | 909.7 ms | 5.12 ms |  3.17 | 63992232 B |
|      StringIntern_Instance | 2M (20k-Values) | 298.6 ms | 2.68 ms |  1.04 |   639920 B |
|        StringIntern_Shared | 2M (20k-Values) | 335.0 ms | 5.76 ms |  1.17 |    12256 B |
|                            |                 |          |         |       |            |
|                    Default |       2M-Unique | 293.1 ms | 9.50 ms |  1.00 | 79199856 B |
| MTHP_StringPool_Stackalloc |       2M-Unique | 857.9 ms | 7.59 ms |  2.93 | 79199856 B |
|    MTHP_StringPool_Encoder |       2M-Unique | 920.4 ms | 8.07 ms |  3.14 | 79200144 B |
|      StringIntern_Instance |       2M-Unique | 480.7 ms | 8.58 ms |  1.64 | 79199856 B |
|        StringIntern_Shared |       2M-Unique | 962.6 ms | 8.40 ms |  3.29 | 79200144 B |
|                            |                 |          |         |       |            |
|                    Default |       Taxi-data | 329.7 ms | 6.03 ms |  1.00 | 67108800 B |
| MTHP_StringPool_Stackalloc |       Taxi-data | 301.8 ms | 2.14 ms |  0.92 |      224 B |
|    MTHP_StringPool_Encoder |       Taxi-data | 346.4 ms | 2.27 ms |  1.05 |      512 B |
|      StringIntern_Instance |       Taxi-data | 300.3 ms | 2.71 ms |  0.91 |      224 B |
|        StringIntern_Shared |       Taxi-data | 318.1 ms | 2.51 ms |  0.97 |      320 B |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on contributing to this project.

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) 
to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](http://www.dotnetfoundation.org/code-of-conduct).

## License

This project is licensed with the [Apache License](LICENSE).
