// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Ben.Collections.Specialized
{
    /// <summary>
    /// Class to use the shared Intern pool via using static
    /// </summary>
    public class StringCache
    {
        public static bool Contains(string item)
            => InternPool.Shared.Contains(item);

        public static string Intern(ReadOnlySpan<char> value)
            => InternPool.Shared.Intern(value);

#if !NETSTANDARD2_0
        [return: NotNullIfNotNull("value")]
#endif
        public static string? Intern(string? value)
            => InternPool.Shared.Intern(value);

        public static string Intern(byte[]? value, Encoding encoding)
            => InternPool.Shared.Intern(value.AsSpan(), encoding);

        public static string Intern(ReadOnlySpan<byte> value, Encoding encoding)
            => InternPool.Shared.Intern(value, encoding);

        public static string InternUtf8(ReadOnlySpan<byte> utf8Value)
            => InternPool.Shared.InternUtf8(utf8Value);
    }
}
