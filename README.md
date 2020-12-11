# Ben.StringIntern

[![NuGet version (Ben.StringIntern)](https://img.shields.io/nuget/v/Ben.StringIntern.svg?style=flat-square)](https://www.nuget.org/packages/Ben.StringIntern/)

Inspired by this issue being closed: "API request: string.Intern(ReadOnlySpan<char> ...)" [#28368](https://github.com/dotnet/runtime/issues/28368)

```csharp
namespace Ben.Collections.Specialized
{
    public class InternPool
    {
        public string InternAscii(ReadOnlySpan<byte> asciiValue);
        
        public string InternUtf8(ReadOnlySpan<byte> utf8Value);
        
        public string Intern(ReadOnlySpan<char> value);
        
        [return: NotNullIfNotNull("value")]
        public string? Intern(string? value);
    }
}
```

## Todo

- [x] Add some tests
- [ ] Add more tests
- [x] Add a "high water mark" max interned count and evict based on LRU on new add
- [ ] Add a "low water mark" interned count and evict based over that on LRU at Gen2
- [ ] Add a max size (string Length) to intern option
- [ ] Add a `.Shared` global pool that is "threadsafe"

## Building

`dotnet build -c Release`

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on contributing to this project.

This project has adopted the code of conduct defined by the [Contributor Covenant](http://contributor-covenant.org/) 
to clarify expected behavior in our community. For more information, see the [.NET Foundation Code of Conduct](http://www.dotnetfoundation.org/code-of-conduct).

## License

This project is licensed with the [Apache License](LICENSE).
