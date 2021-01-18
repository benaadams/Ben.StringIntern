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

|                Method |              Dataset |       Mean |    Error | Ratio |   Allocated |
|---------------------- |--------------------- |-----------:|---------:|------:|------------:|
|               Default |      2M (20k-Values) |   284.7 ms |  5.30 ms |  1.00 |  63991944 B |
| StringIntern_Instance |      2M (20k-Values) |   295.3 ms |  4.21 ms |  1.04 |    639920 B |
|   StringIntern_Shared |      2M (20k-Values) |   330.3 ms |  4.79 ms |  1.16 |     12256 B |
|                       |                      |            |          |       |             |
|               Default |            2M-Unique |   292.3 ms |  7.76 ms |  1.00 |  79199856 B |
| StringIntern_Instance |            2M-Unique |   472.7 ms |  7.33 ms |  1.62 |  79199856 B |
|   StringIntern_Shared |            2M-Unique | 1,035.4 ms | 11.56 ms |  3.54 |  79200144 B |
|                       |                      |            |          |       |             |
|               Default |            Taxi-data |   327.3 ms |  4.23 ms |  1.00 |  67108800 B |
| StringIntern_Instance |            Taxi-data |   291.2 ms |  1.16 ms |  0.89 |       224 B |
|   StringIntern_Shared |            Taxi-data |   311.9 ms |  2.34 ms |  0.95 |       456 B |
|                       |                      |            |          |       |             |
|               Default |       2M-Unique-Long |   836.2 ms | 10.45 ms |  1.00 | 460444288 B |
| StringIntern_Instance |       2M-Unique-Long | 1,242.7 ms |  8.06 ms |  1.49 | 460444288 B |
|   StringIntern_Shared |       2M-Unique-Long | 1,734.8 ms | 12.35 ms |  2.07 | 460444784 B |
|                       |                      |            |          |       |             |
|               Default | 2M (20k-Values-Long) |   736.5 ms | 33.14 ms |  1.00 | 332438368 B |
| StringIntern_Instance | 2M (20k-Values-Long) |   368.1 ms |  2.59 ms |  0.50 |   3324384 B |
|   StringIntern_Shared | 2M (20k-Values-Long) |   413.4 ms |  4.13 ms |  0.56 |     32200 B |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on contributing to this project.

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) 
to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](http://www.dotnetfoundation.org/code-of-conduct).

## License

This project is licensed with the [Apache License](LICENSE).
