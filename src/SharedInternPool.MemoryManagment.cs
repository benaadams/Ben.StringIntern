// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;

namespace Ben.Collections.Specialized
{
    public partial class SharedInternPool : IInternPool
    {
        private class InternPoolCleaner
#if NET5_0 || NETCOREAPP3_1
            : IThreadPoolWorkItem
#endif
        {
            private SharedInternPool _pool;
            private int _isTrimming = 0;
            private long _collections = 0;

            public long Collections => Volatile.Read(ref _collections);

            public InternPoolCleaner(SharedInternPool pool)
            {
                _pool = pool;
            }

            public void Execute()
            {
                if (Interlocked.Exchange(ref _isTrimming, 1) == 1)
                    return;

                _pool.Trim((InternPool.TrimLevel)((int)_collections % (int)InternPool.TrimLevel.Max));

                Interlocked.Increment(ref _collections);

                Volatile.Write(ref _isTrimming, 0);
            }
        }

        internal long Collections => _cleaner?.Collections ?? 0;

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
            // Enqueue trim to ThreadPool
            return ((SharedInternPool)target).EnqueueTrim();
        }

        private InternPoolCleaner? _cleaner;
        private bool EnqueueTrim()
        {
            var cleaner = _cleaner;
            if (cleaner == null)
            {
                cleaner = new InternPoolCleaner(this);
                cleaner = Interlocked.CompareExchange(ref _cleaner, cleaner, null) ?? cleaner;
            }

#if NET5_0 || NETCOREAPP3_1
            ThreadPool.UnsafeQueueUserWorkItem(cleaner, preferLocal: false);
#else
            ThreadPool.UnsafeQueueUserWorkItem((o) => ((InternPoolCleaner)o).Execute(), cleaner);
#endif
            return true;
        }

        private bool Trim(InternPool.TrimLevel level)
        {
            var pools = _pools;
#if NET5_0 || NETCOREAPP3_1
            MemoryPressure pressure = GetMemoryPressure();
            if (pressure == MemoryPressure.High)
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
                            _totalEvictedCount += pool.Count + pool.Evicted;
                            pools[i] = null;
                        }

                    }
                }
            }
            else
            {
#endif
                for (int i = 0; i < pools.Length; i++)
                {
                    var pool = pools[i];
                    if (pool != null)
                    {
                        lock (pool)
                        {
                            pool.Trim(level);
                        }

                    }
                }
#if NET5_0 || NETCOREAPP3_1
            }
#endif

            return true;
        }

#if NET5_0 || NETCOREAPP3_1
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
#endif

        private enum MemoryPressure
        {
            Low,
            Medium,
            High
        }
    }
}
