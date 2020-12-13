# Ben.StringIntern

[![NuGet version (Ben.StringIntern)](https://img.shields.io/nuget/v/Ben.StringIntern.svg?style=flat-square)](https://www.nuget.org/packages/Ben.StringIntern/)
![.NET 5.0](https://github.com/benaadams/Ben.StringIntern/workflows/.NET%205.0/badge.svg)
![.NET Core 3.1](https://github.com/benaadams/Ben.StringIntern/workflows/.NET%20Core%203.1/badge.svg)

Inspired by this issue being closed: "API request: string.Intern(ReadOnlySpan<char> ...)" [#28368](https://github.com/dotnet/runtime/issues/28368)

## Example usuage

Sql query
```csharp
while (await reader.ReadAsync())
{
    var id = reader.GetInt32(0);
    
    // Dedupe the string before you keep it
    var category = InternPool.Shared.Intern(reader.GetString(1));
    
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
    Considered (Count / 1 sec)                     4,493
    Deduped (Count / 1 sec)                        4,492
    Total Considered                             699,741
    Total Count                                   36,045
    Total Deduped                                663,696
```

## Todo

- [ ] Add a "low water mark" interned count and evict based over that on LRU at Gen2

## Building

`dotnet build -c Release`

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on contributing to this project.

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) 
to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](http://www.dotnetfoundation.org/code-of-conduct).

## License

This project is licensed with the [Apache License](LICENSE).
