// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
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

            // Calcuate hash outside lock
            var hashCode = pool.GetHashCode(value, out bool randomisedHash);

            return InternSynchronized(pool, hashCode, randomisedHash, value);
        }

#if !NETSTANDARD2_0
        [return: NotNullIfNotNull("value")]
#endif
        public string? Intern(string? value)
        {
            if (value is null) return null;
            if (value.Length == 0) return string.Empty;

            if (value.Length > MaxLength) return value;

            var firstChar = value[0];
            var pool = GetPool(firstChar);

            // Calcuate hash outside lock
            var span = value.AsSpan();
            var hashCode = pool.GetHashCode(span, out bool randomisedHash);

            return InternSynchronized(pool, hashCode, randomisedHash, span);
        }

        public unsafe string InternAscii(ReadOnlySpan<byte> asciiValue)
        {
            if (asciiValue.Length == 0) return string.Empty;
#if !NETSTANDARD2_0
            if (asciiValue.Length > MaxLength) return Encoding.ASCII.GetString(asciiValue);
#else
            fixed (byte* pValue = &MemoryMarshal.GetReference(asciiValue))
            {
                if (asciiValue.Length > MaxLength) return Encoding.ASCII.GetString(pValue, asciiValue.Length);
            }
#endif
            char firstChar = (char)asciiValue[0];

            var pool = GetPool(firstChar);

            lock (pool)
            {
                return pool.InternAscii(asciiValue);
            }
        }

        public string Intern(byte[]? value, Encoding encoding)
            => Intern(value.AsSpan(), encoding);

#if !NETSTANDARD2_0
        public string Intern(ReadOnlySpan<byte> value, Encoding encoding)
        {
            if (value.Length == 0) return string.Empty;
            int count = encoding.GetMaxCharCount(value.Length);
            if (count > MaxLength)
                return encoding.GetString(value);

            char[]? array = null;

            if (count > InternPool.StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(count);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[InternPool.StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[count] : array;
#endif

            count = encoding.GetChars(value, span);
            span = span.Slice(0, count);

            var pool = GetPool(span[0]);

            // Calcuate hash outside lock
            var hashCode = pool.GetHashCode(span, out bool randomisedHash);

            var result = InternSynchronized(pool, hashCode, randomisedHash, span);
            if (array != null)
            {
                ArrayPool<char>.Shared.Return(array);
            }

            return result;
        }
#else
        public unsafe string Intern(ReadOnlySpan<byte> value, Encoding encoding)
        {
            fixed (byte* pValue = &MemoryMarshal.GetReference(value))
            {
                if (value.Length == 0) return string.Empty;
                int count = encoding.GetMaxCharCount(value.Length);
                if (count > MaxLength)
                    return encoding.GetString(pValue, value.Length);

                char[]? array = null;

                if (count > InternPool.StackAllocThresholdChars)
                {
                    array = ArrayPool<char>.Shared.Rent(count);
                }

                Span<char> span = array is null ? stackalloc char[count] : array;

                fixed (char* pOutput = &MemoryMarshal.GetReference(span))
                {
                    count = encoding.GetChars(pValue, value.Length, pOutput, span.Length);
                }

                span = span.Slice(0, count);

                var pool = GetPool(span[0]);

                // Calcuate hash outside lock
                var hashCode = pool.GetHashCode(span, out bool randomisedHash);

                var result = InternSynchronized(pool, hashCode, randomisedHash, span);
                if (array != null)
                {
                    ArrayPool<char>.Shared.Return(array);
                }

                return result;
            }
        }
#endif

#if !NETSTANDARD2_0
        public string InternUtf8(ReadOnlySpan<byte> utf8Value)
        {
            if (utf8Value.Length == 0) return string.Empty;
            int count = utf8Value.Length * 2; // 2 x length for invalid sequence encoding
            if (count > MaxLength)
                return Encoding.UTF8.GetString(utf8Value);

            char[]? array = null;

            if (count > InternPool.StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(count);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[InternPool.StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[count] : array;
#endif

#if NET5_0 || NETCOREAPP3_1
            Utf8.ToUtf16(utf8Value, span, out _, out count);
#else
            count = Encoding.UTF8.GetChars(utf8Value, span);
#endif
            span = span.Slice(0, count);

            var pool = GetPool(span[0]);

            // Calcuate hash outside lock
            var hashCode = pool.GetHashCode(span, out bool randomisedHash);

            var result = InternSynchronized(pool, hashCode, randomisedHash, span);
            if (array != null)
            {
                ArrayPool<char>.Shared.Return(array);
            }

            return result;
        }
#else
        public unsafe string InternUtf8(ReadOnlySpan<byte> utf8Value)
        {
            fixed (byte* pValue = &MemoryMarshal.GetReference(utf8Value))
            {
                if (utf8Value.Length == 0) return string.Empty;
                int count = Encoding.UTF8.GetMaxCharCount(utf8Value.Length);
                if (count > MaxLength)
                    return Encoding.UTF8.GetString(pValue, utf8Value.Length);

                char[]? array = null;

                if (count > InternPool.StackAllocThresholdChars)
                {
                    array = ArrayPool<char>.Shared.Rent(count);
                }

                Span<char> span = array is null ? stackalloc char[count] : array;

                fixed (char* pOutput = &MemoryMarshal.GetReference(span))
                {
                    count = Encoding.UTF8.GetChars(pValue, utf8Value.Length, pOutput, span.Length);
                }

                span = span.Slice(0, count);

                var pool = GetPool(span[0]);

                // Calcuate hash outside lock
                var hashCode = pool.GetHashCode(span, out bool randomisedHash);

                var result = InternSynchronized(pool, hashCode, randomisedHash, span);
                if (array != null)
                {
                    ArrayPool<char>.Shared.Return(array);
                }

                return result;
            }
        }
#endif

        private static string InternSynchronized(InternPool pool, int hashCode, bool randomisedHash, ReadOnlySpan<char> span)
        {
            lock (pool)
            {
                return pool.Intern(hashCode, randomisedHash, span);
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
                Gen2GcCallback.Register(self => ((SharedInternPool)self).EnqueueTrim(), this);
            }

            return pool;
        }
    }
}
