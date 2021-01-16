// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace Ben.Collections.Specialized
{
    public interface IInternPool
    {
        long Added { get; }
        long Considered { get; }
        int Count { get; }
        long Deduped { get; }

        bool Contains(string item);

        string Intern(ReadOnlySpan<char> value);
        string? Intern(string? value);
        string InternAscii(ReadOnlySpan<byte> asciiValue);
        string InternUtf8(ReadOnlySpan<byte> utf8Value);

#if NET5_0 || NETCOREAPP3_1
        string Intern(char[] value) => Intern(new ReadOnlySpan<char>(value));
        string InternAscii(byte[] asciiValue) => InternAscii(new ReadOnlySpan<byte>(asciiValue));
        string InternUtf8(byte[] utf8Value) => InternUtf8(new ReadOnlySpan<byte>(utf8Value));
#endif
    }
}