using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeWorldBuilder.Core.Service
{
    public static class ServiceRegistry
    {
        static Dictionary<Type, object> services = new();

        public static void Register<T>(T service) where T : class
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            services[typeof(T)] = service;
        }

        public static T Resolve<T>() where T : class
        {
            if (services.TryGetValue(typeof(T), out var s)) return (T)s;
            throw new InvalidOperationException($"Service not registered: {typeof(T).Name}");
        }

        public static bool TryResolve<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out var s)) { service = (T)s; return true; }
            service = null;
            return false;
        }

        public static void UnRegister<T>() where T : class => services.Remove(typeof(T));

        public static void Clear() => services.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => services.Clear();
    }
}