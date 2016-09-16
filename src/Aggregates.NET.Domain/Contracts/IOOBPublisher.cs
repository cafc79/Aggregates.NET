﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IOOBPublisher
    {
        Task Publish<T>(String Bucket, String StreamId, IEnumerable<IWritableEvent> Events, IDictionary<String, String> commitHeaders) where T : class, IEventSource;
        Task<IEnumerable<IWritableEvent>> Retrieve<T>(String Bucket, String StreamId, Int32 Skip = 0, Int32 Take = -1) where T : class, IEventSource;
    }
}
