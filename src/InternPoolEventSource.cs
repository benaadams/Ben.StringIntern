// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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

        private PollingCounter? _poolSize;


        private long Added { get { UpdateStats(); return _stats.Added; } }
        private long Considered { get { UpdateStats(); return _stats.Considered; } }
        private int Count { get { UpdateStats(); return _stats.Count; } }
        private long Deduped { get { UpdateStats(); return _stats.Deduped; } }

        private long _lastCheck;
        private long _lastUpdate;
        private SharedInternPool.StatsSnapshot _stats;

        private InternPoolEventSource() : base("InternPool")
        {
        }

        private void UpdateStats()
        {
            var lastCheck = _lastCheck;
            var newCheck = Environment.TickCount64;
            if (lastCheck == Interlocked.CompareExchange(ref _lastCheck, newCheck, lastCheck))
            {
                if (newCheck - _lastUpdate > 1000)
                {
                    _stats = _pool!.Stats;
                    _lastUpdate = newCheck;
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

                _consideredPerSec ??= new IncrementingPollingCounter("considered-per-second", this, () => Considered)
                {
                    DisplayName = "Considered"
                };

                _dedupedPerSec ??= new IncrementingPollingCounter("deduped-per-second", this, () => Deduped)
                {
                    DisplayName = "Deduped"
                };

                _poolSize ??= new PollingCounter("count", this, () => Count)
                {
                    DisplayName = "Total Count",
                };
            }
        }
    }
}