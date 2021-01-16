// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Ben.Collections.Specialized
{
    /// <summary>
    /// Static class to use <seealso cref="InternPool.Shared"/> via "using static"
    /// </summary>
    public static class StringCache
    {
        /// <summary>Determines whether the <seealso cref="InternPool.Shared"/> contains the specified element.</summary>
        /// <param name="item">The element to locate in the <seealso cref="InternPool.Shared"/>.</param>
        /// <returns>true if the <seealso cref="InternPool.Shared"/> contains the specified element; otherwise, false.</returns>
        public static bool Contains(string item)
            => InternPool.Shared.Contains(item);

        /// <summary>
        /// Adds a new string to the <seealso cref="InternPool.Shared"/> if it's not already contained;
        /// returns the existing materialized string if it already exists.
        /// </summary>
        /// <param name="value">The char sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public static string Intern(ReadOnlySpan<char> value)
            => InternPool.Shared.Intern(value);

        /// <summary>
        /// Adds a new string to the <seealso cref="InternPool.Shared"/> if it's not already contained;
        /// returns the existing string if it already exists.
        /// </summary>
        /// <param name="value">The string to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
#if !NETSTANDARD2_0
        [return: NotNullIfNotNull("value")]
#endif
        public static string? Intern(string? value)
            => InternPool.Shared.Intern(value);

        /// <summary>
        /// Adds a new string to the <seealso cref="InternPool.Shared"/> if it's not already contained;
        /// returns the existing materialized string if it already exists.
        /// </summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <param name="encoding">The encoding to use when converting the bytes to chars.</param>
        /// <returns>The interned string.</returns>
        public static string Intern(byte[]? value, Encoding encoding)
            => InternPool.Shared.Intern(value.AsSpan(), encoding);

        /// <summary>
        /// Adds a new string to the <seealso cref="InternPool.Shared"/> if it's not already contained;
        /// returns the existing materialized string if it already exists.
        /// </summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <param name="encoding">The encoding to use when converting the bytes to chars.</param>
        /// <returns>The interned string.</returns>
        public static string Intern(ReadOnlySpan<byte> value, Encoding encoding)
            => InternPool.Shared.Intern(value, encoding);

        /// <summary>
        /// Adds a new string to the <seealso cref="InternPool.Shared"/> if it's not already contained;
        /// returns the existing materialized string if it already exists.
        /// </summary>
        /// <param name="value">The utf8 byte sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public static string InternUtf8(ReadOnlySpan<byte> utf8Value)
            => InternPool.Shared.InternUtf8(utf8Value);
    }
}
