﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Contracts;
using Aggregates.Exceptions;
using Aggregates.Extensions;
using Aggregates.Logging;

namespace Aggregates.Internal
{
    class DelayedChannel : IDelayedChannel
    {
        private class InFlightInfo
        {
            public DateTime At { get; set; }
            public int Position { get; set; }
        }


        private static readonly ILog Logger = LogProvider.GetLogger("DelayedChannel");
        private static readonly ILog SlowLogger = LogProvider.GetLogger("Slow Alarm");

        private readonly IDelayedCache _cache;

        private ConcurrentDictionary<Tuple<string, string>, List<IDelayedMessage>> _inFlightMemCache;
        private ConcurrentDictionary<Tuple<string, string>, List<IDelayedMessage>> _uncommitted;


        public DelayedChannel(IDelayedCache cache)
        {
            _cache = cache;
        }


        public Task Begin()
        {
            _uncommitted = new ConcurrentDictionary<Tuple<string, string>, List<IDelayedMessage>>();
            _inFlightMemCache = new ConcurrentDictionary<Tuple<string, string>, List<IDelayedMessage>>();
            return Task.CompletedTask;
        }

        public async Task End(Exception ex = null)
        {

            if (ex != null)
            {
                Logger.InfoEvent("UOWException", "{InFlight} messages back into cache", _inFlightMemCache.Count);
                foreach (var inflight in _inFlightMemCache)
                {
                    await _cache.Add(inflight.Key.Item1, inflight.Key.Item2, inflight.Value.ToArray()).ConfigureAwait(false);
                }
            }

            if (ex == null)
            {
                Logger.DebugEvent("UOWEnd", "{Uncommitted} streams into mem cache");

                _inFlightMemCache.Clear();

                foreach (var kv in _uncommitted)
                {
                    if (!kv.Value.Any())
                        return;

                    await _cache.Add(kv.Key.Item1, kv.Key.Item2, kv.Value.ToArray());
                }
            }
        }

        public async Task<TimeSpan?> Age(string channel, string key = null)
        {

            var specificAge = await _cache.Age(channel, key).ConfigureAwait(false);
            
            if (specificAge > TimeSpan.FromMinutes(1))
                SlowLogger.InfoEvent("Age", "Channel [{Channel}] specific [{Key}] age {Seconds} seconds", channel, key, specificAge?.TotalSeconds);

            return specificAge;
        }

        public async Task<int> Size(string channel, string key = null)
        {

            var specificSize = await _cache.Size(channel, key).ConfigureAwait(false);

            var specificKey = new Tuple<string, string>(channel, key);
            if (_uncommitted.ContainsKey(specificKey))
                specificSize += _uncommitted[specificKey].Count;

            if (specificSize > 5000)
                SlowLogger.InfoEvent("Size", "Channel [{Channel}] specific [{key}] size {Size}", channel, key, specificSize);

            return specificSize;
        }

        public Task AddToQueue(string channel, IDelayedMessage queued, string key = null)
        {
            var specificKey = new Tuple<string, string>(channel, key);

            _uncommitted.AddOrUpdate(specificKey, new List<IDelayedMessage> { queued }, (k, existing) => {
                existing.Add(queued);
                return existing;
            });


            return Task.CompletedTask;
        }

        public async Task<IEnumerable<IDelayedMessage>> Pull(string channel, string key = null, int? max = null)
        {
            var specificKey = new Tuple<string, string>(channel, key);

            var fromCache = await _cache.Pull(channel, key, max).ConfigureAwait(false);

            List<IDelayedMessage> discovered = new List<IDelayedMessage>(fromCache);

            List<IDelayedMessage> fromUncommitted;
            if (_uncommitted.TryRemove(specificKey, out fromUncommitted))
                discovered.AddRange(fromUncommitted);

            if(discovered.Any())
                _inFlightMemCache.TryAdd(specificKey, discovered);

            Logger.InfoEvent("Pull", "{Messages} from channel [{Channel}] key [{key}]", discovered.Count, channel, key);
            return discovered;
        }


    }
}
