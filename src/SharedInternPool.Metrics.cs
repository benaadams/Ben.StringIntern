// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;

namespace Ben.Collections.Specialized
{
    public partial class SharedInternPool
    {
        private long _totalAdded = 0;
        private long _totalConsidered = 0;
        private long _totalDeduped = 0;
        private long _totalEvictedCount = 0;

        public StatsSnapshot Stats
        {
            get
            {
                long totalAdded = Volatile.Read(ref _totalAdded);
                long totalConsidered = Volatile.Read(ref _totalConsidered);
                long totalDeduped = Volatile.Read(ref _totalDeduped);
                long totalEvicted = Volatile.Read(ref _totalEvictedCount);
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

                return new StatsSnapshot(totalAdded, totalConsidered, totalCount, totalDeduped, totalEvicted);
            }
        }

        public long Added
        {
            get
            {
                long total = Volatile.Read(ref _totalAdded);
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
                long total = Volatile.Read(ref _totalConsidered);
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
                long total = Volatile.Read(ref _totalDeduped);
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

        public long Evicted
        {
            get
            {
                long total = Volatile.Read(ref _totalEvictedCount);
                foreach (InternPool? pool in _pools)
                {
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            total += pool.Evicted;
                        }
                    }
                }

                return total;
            }
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
