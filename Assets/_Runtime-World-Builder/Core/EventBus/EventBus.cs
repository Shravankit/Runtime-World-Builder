using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.Core.EventBus
{
    public sealed class EventBus
    {
        readonly Dictionary<Type, Delegate> handlers = new();

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            handlers.TryGetValue(typeof(T), out var existing);
            handlers[typeof(T)] = Delegate.Combine(existing, handler);
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (!handlers.TryGetValue(typeof(T), out var existing)) return;
            var updated = Delegate.Remove(existing, handler);
            if (updated == null) handlers.Remove(typeof(T));
            else handlers[typeof(T)] = updated;
        }

        public void Publish<T>(in T evt) where T : struct
        {
            if (handlers.TryGetValue(typeof(T), out var d)) ((Action<T>)d)(evt);
        }

        public void Clear() => handlers.Clear();
    }
}