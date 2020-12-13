// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using Ben.Collections.Specialized;

namespace Ben.Collections
{
    public static class InternPoolExtensions
    {
        public static string[] ToInternedArray(this string[] array)
        {
            var pool = InternPool.Shared;

            var newArray = new string[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                newArray[i] = pool.Intern(array[i]);
            }

            return newArray;
        }

        public static List<string> ToInternedList(this List<string> list)
        {
            var pool = InternPool.Shared;

            var internedList = new List<string>(list.Count);
            var count = list.Count;
            for (int i = 0; i < count; i++)
            {
                internedList.Add(pool.Intern(list[i]));
            }

            return internedList;
        }

        public static List<string> ToInternedList(this IList<string> list)
        {
            var pool = InternPool.Shared;

            var internedList = new List<string>(list.Count);
            var count = list.Count;
            for (int i = 0; i < count; i++)
            {
                internedList.Add(pool.Intern(list[i]));
            }

            return internedList;
        }

        public static List<string> ToInternedList(this ICollection<string> collection)
        {
            var pool = InternPool.Shared;

            var internedList = new List<string>(collection.Count);
            foreach (var item in collection)
            {
                internedList.Add(pool.Intern(item));

            }

            return internedList;
        }

        public static Dictionary<string, string> ToInternedDictionary(this Dictionary<string, string> dict)
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<string, string>(dict.Count, dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(pool.Intern(kv.Key), pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static Dictionary<string, TValue> ToInternedDictionary<TValue>(this Dictionary<string, TValue> dict)
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<string, TValue>(dict.Count, dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(pool.Intern(kv.Key), kv.Value);
            }

            return internedDict;
        }

        public static Dictionary<TKey, string> ToInternedDictionary<TKey>(this Dictionary<TKey, string> dict)
             where TKey : notnull
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<TKey, string>(dict.Count, dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(kv.Key, pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static Dictionary<string, string> ToInternedDictionary(this IDictionary<string, string> dict, IEqualityComparer<string> comparer)
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<string, string>(dict.Count, comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(pool.Intern(kv.Key), pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static Dictionary<string, TValue> ToInternedDictionary<TValue>(this IDictionary<string, TValue> dict, IEqualityComparer<string> comparer)
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<string, TValue>(dict.Count, comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(pool.Intern(kv.Key), kv.Value);
            }

            return internedDict;
        }

        public static Dictionary<TKey, string> ToInternedDictionary<TKey>(this IDictionary<TKey, string> dict, IEqualityComparer<TKey> comparer)
             where TKey : notnull
        {
            var pool = InternPool.Shared;

            var internedDict = new Dictionary<TKey, string>(dict.Count, comparer);
            foreach (var kv in dict)
            {
                internedDict.Add(kv.Key, pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static ConcurrentDictionary<string, string> ToInternedConcurrentDictionary(this Dictionary<string, string> dict)
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<string, string>(dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(pool.Intern(kv.Key)!, pool.Intern(kv.Value)!);
            }

            return internedDict;
        }

        public static ConcurrentDictionary<string, TValue> ToInternedConcurrentDictionary<TValue>(this Dictionary<string, TValue> dict)
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<string, TValue>(dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(pool.Intern(kv.Key), kv.Value);
            }

            return internedDict;
        }

        public static ConcurrentDictionary<TKey, string> ToInternedConcurrentDictionary<TKey>(this Dictionary<TKey, string> dict)
             where TKey : notnull
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<TKey, string>(dict.Comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(kv.Key, pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static ConcurrentDictionary<string, string> ToInternedConcurrentDictionary(this IDictionary<string, string> dict, IEqualityComparer<string> comparer)
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<string, string>(comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(pool.Intern(kv.Key), pool.Intern(kv.Value));
            }

            return internedDict;
        }

        public static ConcurrentDictionary<string, TValue> ToInternedConcurrentDictionary<TValue>(this IDictionary<string, TValue> dict, IEqualityComparer<string> comparer)
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<string, TValue>(comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(pool.Intern(kv.Key)!, kv.Value);
            }

            return internedDict;
        }

        public static ConcurrentDictionary<TKey, string> ToInternedConcurrentDictionary<TKey>(this IDictionary<TKey, string> dict, IEqualityComparer<TKey> comparer)
             where TKey : notnull
        {
            var pool = InternPool.Shared;

            var internedDict = new ConcurrentDictionary<TKey, string>(comparer);
            foreach (var kv in dict)
            {
                internedDict.TryAdd(kv.Key, pool.Intern(kv.Value));
            }

            return internedDict;
        }
    }
}
