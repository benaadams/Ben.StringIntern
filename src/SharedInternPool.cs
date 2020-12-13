// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
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

        long _evictedCount = 0;

        public bool Trim()
        {
            int milliseconds = Environment.TickCount;
            MemoryPressure pressure = GetMemoryPressure();

            var pools = _pools;

            long evictedCount = 0;
            //if (pressure == MemoryPressure.High)
            {
                // Under high pressure, release everything
                for (int i = 0; i < pools.Length; i++)
                {
                    var pool = pools[i];
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            _totalAdded += pool.Added;
                            _totalConsidered += pool.Considered;
                            _totalDeduped += pool.Deduped;
                            evictedCount += pool.Count + pool.Evicted;
                            pools[i] = null;
                        }

                    }
                }
            }
            _evictedCount += evictedCount;
            return true;
        }

        private enum MemoryPressure
        {
            Low,
            Medium,
            High
        }

        private static MemoryPressure GetMemoryPressure()
        {
            const double HighPressureThreshold = .90;       // Percent of GC memory pressure threshold we consider "high"
            const double MediumPressureThreshold = .70;     // Percent of GC memory pressure threshold we consider "medium"

            GCMemoryInfo memoryInfo = GC.GetGCMemoryInfo();
            if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * HighPressureThreshold)
            {
                return MemoryPressure.High;
            }
            else if (memoryInfo.MemoryLoadBytes >= memoryInfo.HighMemoryLoadThresholdBytes * MediumPressureThreshold)
            {
                return MemoryPressure.Medium;
            }
            return MemoryPressure.Low;
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

        private long _totalAdded = 0;
        private long _totalConsidered = 0;
        private long _totalDeduped = 0;

        public StatsSnapshot Stats
        {
            get
            {
                long totalAdded = _totalAdded;
                long totalConsidered = _totalConsidered;
                long totalDeduped = _totalDeduped;
                long totalEvicted = _evictedCount;
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
                            totalEvicted += pool.Evicted;
                        }
                    }
                }

                return new StatsSnapshot(totalAdded, totalConsidered, totalCount, totalDeduped, _evictedCount);
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

        /// <summary>
        /// This is the static function that is called from the gen2 GC callback.
        /// The input object is the instance we want the callback on.
        /// </summary>
        /// <remarks>
        /// The reason that we make this function static and take the instance as a parameter is that
        /// we would otherwise root the instance to the Gen2GcCallback object, leaking the instance even when
        /// the application no longer needs it.
        /// </remarks>
        private static bool Gen2GcCallbackFunc(object target)
        {
            return ((SharedInternPool)target).Trim();
        }

        public readonly struct StatsSnapshot
        {
            internal StatsSnapshot(long added, long considered, int count, long deduped, long evicted)
            {
                Added = added;
                Considered = considered;
                Count = count;
                Deduped = deduped;
                Evicted = evicted;
            }

            public long Added { get; }
            public long Considered { get; }
            public int Count { get; }
            public long Deduped { get; }
            public long Evicted { get; }
        }
    }

}
