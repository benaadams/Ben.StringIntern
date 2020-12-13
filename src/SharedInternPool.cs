// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

namespace Ben.Collections.Specialized
{
    public class SharedInternPool : IInternPool
    {
        private const int NumberOfPools = 32;
        private const int NumberOfPoolsMask = NumberOfPools - 1;
        private const int MaxLength = 640;

        private readonly InternPool?[] _pools = new InternPool[NumberOfPools];

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
            if (Encoding.UTF8.GetMaxCharCount(utf8Value.Length) > MaxLength)
                return Encoding.UTF8.GetString(utf8Value);

            Span<char> firstChars = stackalloc char[2];
            Encoding.UTF8.GetChars(utf8Value.Slice(0, 4), firstChars);

            var pool = GetPool(firstChars[0]);

            lock (pool)
            {
                return pool.InternUtf8(utf8Value);
            }
        }

        public long Added
        {
            get
            {
                long total = 0;
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            total += pool.Added;
                        }
                    }
                }

                return total;
            }
        }

        public long Considered
        {
            get
            {
                long total = 0;
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            total += pool.Considered;
                        }
                    }
                }

                return total;
            }
        }

        public int Count
        {
            get
            {
                int total = 0;
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            total += pool.Count;
                        }
                    }
                }

                return total;
            }
        }

        public long Deduped
        {
            get
            {
                long total = 0;
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            total += pool.Deduped;
                        }
                    }
                }

                return total;
            }
        }

        public StatsSnapshot Stats
        {
            get
            {
                long totalAdded = 0;
                long totalConsidered = 0;
                long totalDeduped = 0;
                int totalCount = 0;
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            totalAdded += pool.Added;
                            totalConsidered += pool.Considered;
                            totalDeduped += pool.Deduped;
                            totalCount += pool.Count;
                        }
                    }
                }

                return new StatsSnapshot(totalAdded, totalConsidered, totalCount, totalDeduped);
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
            return Interlocked.CompareExchange(ref _pools[poolIndex], pool, null) ?? pool;
        }

        public readonly struct StatsSnapshot
        {
            internal StatsSnapshot(long added, long considered, int count, long deduped)
            {
                Added = added;
                Considered = considered;
                Count = count;
                Deduped = deduped;
            }

            public long Added { get; }
            public long Considered { get; }
            public int Count { get; }
            public long Deduped { get; }
        }
    }

}
