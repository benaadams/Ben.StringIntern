// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

namespace Ben.Collections.Specialized
{
    public partial class SharedInternPool : IInternPool
    {
        private const int NumberOfPools = 32;
        private const int NumberOfPoolsMask = NumberOfPools - 1;
        private const int MaxLength = 640;

        private readonly InternPool?[] _pools = new InternPool[NumberOfPools];
        private int _callbackCreated;

        public bool Contains(string item)
        {
            if (string.IsNullOrEmpty(item)) return true;

            if (item.Length > MaxLength) return false;

            var pool = _pools[item[0]];

            if (pool is null) return false;

            lock (pool)
            {
                return pool.Contains(item);
            }
        }

        public string Intern(ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return string.Empty;
            if (value.Length > MaxLength) return value.ToString();

            var firstChar = value[0];
            var pool = GetPool(firstChar);

            lock (pool)
            {
                return pool.Intern(value);
            }
        }

        [return: NotNullIfNotNull("value")]
        public string? Intern(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > MaxLength) return value;

            var firstChar = value[0];
            var pool = GetPool(firstChar);

            lock (pool)
            {
                return pool.Intern(value);
            }
        }

        public unsafe string InternAscii(ReadOnlySpan<byte> asciiValue)
        {
            if (asciiValue.Length == 0) return string.Empty;
            if (asciiValue.Length > MaxLength) return Encoding.ASCII.GetString(asciiValue);

            char firstChar;
            Encoding.ASCII.GetChars(asciiValue.Slice(0, 1), new Span<char>(&firstChar, 1));

            var pool = GetPool(firstChar);

            lock (pool)
            {
                return pool.InternAscii(asciiValue);
            }
        }

        public string InternUtf8(ReadOnlySpan<byte> utf8Value)
        {
            if (utf8Value.Length == 0) return string.Empty;
            if (utf8Value.Length * 4 > MaxLength)
                return Encoding.UTF8.GetString(utf8Value);

            char[]? array = null;

            int count = Encoding.UTF8.GetCharCount(utf8Value);
            if (count > MaxLength)
            {
                return Encoding.UTF8.GetString(utf8Value);
            }

            if (count > InternPool.StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(count);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[InternPool.StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[utf8Value.Length] : array;
#endif

            count = Encoding.UTF8.GetChars(utf8Value, span);
            span = span.Slice(0, count);

            var pool = GetPool(span[0]);

            lock (pool)
            {
                return pool.Intern(span);
            }
        }

        private InternPool GetPool(char firstChar)
        {
            int poolIndex = firstChar & NumberOfPoolsMask;
            var pool = _pools[poolIndex];

            if (pool is null)
            {
                return CreatePool(poolIndex);
            }

            return pool;
        }

        private InternPool CreatePool(int poolIndex)
        {
            var pool = new InternPool(1, 10_000, MaxLength);
            pool = Interlocked.CompareExchange(ref _pools[poolIndex], pool, null) ?? pool;

            if (Interlocked.Exchange(ref _callbackCreated, 1) != 1)
            {
                Gen2GcCallback.Register(Gen2GcCallbackFunc, this);
            }

            return pool;
        }
    }
}
