// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Threading;

namespace Ben.Collections.Specialized
{
    internal sealed class InternPoolEventSource : EventSource
    {
        public static readonly InternPoolEventSource Log = new InternPoolEventSource();

        private SharedInternPool? _pool;

        private IncrementingPollingCounter? _consideredPerSec;
        private PollingCounter? _consideredTotal;

        private IncrementingPollingCounter? _dedupedPerSec;
        private PollingCounter? _dedupedTotal;

        private IncrementingPollingCounter? _evictedPerSec;
        private PollingCounter? _evictedTotal;

        private PollingCounter? _poolSize;
        private PollingCounter? _collections;

        private long Added { get { UpdateStats(); return _stats.Added; } }
        private long Considered { get { UpdateStats(); return _stats.Considered; } }
        private int Count { get { UpdateStats(); return _stats.Count; } }
        private long Deduped { get { UpdateStats(); return _stats.Deduped; } }
        private long Evicted { get { UpdateStats(); return _stats.Evicted; } }

        private long _lastUpdate;
        private SharedInternPool.StatsSnapshot _stats;

        private InternPoolEventSource() : base("InternPool")
        {
        }

        private void UpdateStats()
        {
            var newCheck = Environment.TickCount64;
            var lastUpdate = Volatile.Read(ref _lastUpdate);

            if (newCheck - lastUpdate > 1000)
            {
                if (lastUpdate == Interlocked.CompareExchange(ref _lastUpdate, newCheck, lastUpdate))
                {
                    Debug.Assert(_pool != null, "EventSource should be enabled");
                    _stats = _pool.Stats;
                }
            }
        }

        protected override void OnEventCommand(EventCommandEventArgs command)
        {
            if (command.Command == EventCommand.Enable)
            {
                _pool = InternPool.Shared;

                _consideredTotal ??= new PollingCounter("total-considered", this, () => Considered)
                {
                    DisplayName = "Total Considered",
                };
                _dedupedTotal ??= new PollingCounter("total-deduped", this, () => Deduped)
                {
                    DisplayName = "Total Deduped",
                };
                _evictedTotal ??= new PollingCounter("total-evicted", this, () => Evicted)
                {
                    DisplayName = "Total Evicted",
                };
                _collections ??= new PollingCounter("total-collections", this, () => _pool.Collections)
                {
                    DisplayName = "Total Gen2 Sweeps",
                };

                _consideredPerSec ??= new IncrementingPollingCounter("considered-per-second", this, () => Considered)
                {
                    DisplayName = "Considered"
                };

                _dedupedPerSec ??= new IncrementingPollingCounter("deduped-per-second", this, () => Deduped)
                {
                    DisplayName = "Deduped"
                };

                _evictedPerSec ??= new IncrementingPollingCounter("evicted-per-second", this, () => Evicted)
                {
                    DisplayName = "Evicted"
                };

                _poolSize ??= new PollingCounter("count", this, () => Count)
                {
                    DisplayName = "Total Count",
                };
            }
        }
    }
}