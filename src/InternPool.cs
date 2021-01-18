// Copyright (c) Ben Adams. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using System.Threading;

#if NET5
[module: SkipLocalsInit()]
#endif

namespace Ben.Collections.Specialized
{
    [DebuggerDisplay("Count = {Count}")]
    public class InternPool : IInternPool, ICollection<string>, ISet<string>, IReadOnlyCollection<string>
#if NET5_0
        , IReadOnlySet<string>
#endif
    {
        private static SharedInternPool? s_shared;

        public static SharedInternPool Shared
        { 
            get
            {
                var shared = s_shared;
                if (shared != null) return shared;
                return CreateSharedPool();
            } 
        }

        private static SharedInternPool CreateSharedPool()
        {
            var shared = new SharedInternPool();
            return Interlocked.CompareExchange(ref s_shared, shared, null) ?? shared;
        }

        /// <summary>Cutoff point for stackallocs. This corresponds to the number of ints.</summary>
        private const int StackAllocThresholdInts = 128;
        internal const int StackAllocThresholdChars = StackAllocThresholdInts * 2;

        /// <summary>
        /// When constructing a hashset from an existing collection, it may contain duplicates,
        /// so this is used as the max acceptable excess ratio of capacity to count. Note that
        /// this is only used on the ctor and not to automatically shrink if the hashset has, e.g,
        /// a lot of adds followed by removes. Users must explicitly shrink by calling TrimExcess.
        /// This is set to 3 because capacity is acceptable as 2x rounded up to nearest prime.
        /// </summary>
        private const int ShrinkThreshold = 3;
        private const int StartOfFreeList = -3;

        private int[]? _buckets;
        private Entry[]? _entries;

        private ulong _fastModMultiplier;

        private long _lastUse;
        private long _adds;
        private long _evicted;

        private int _count;
        private int _freeList;
        private int _freeCount;
        private int _version;
        private int _maxCount = -1;
        private int _maxLength = -1;

        private bool _randomisedHash;

        private const int ChurnPoolSize = 32;

        private List<(long lastUse, string value)>? _gen0Pool;
        private List<(long lastUse, string value)>? _gen1Pool;

        private List<(long lastUse, string value)>? GetPool(int generation) => generation == 0 ? _gen0Pool : _gen1Pool;
        private static int GetGeneration(long value) => (int)value & 1;

        /// <summary>
        /// Gets the number of strings currently in the pool.
        /// </summary>
        public int Count => _count - _freeCount;
        /// <summary>
        /// Count of strings checked
        /// </summary>
        public long Considered => _lastUse >> 1;
        /// <summary>
        /// Count of strings deduplicated
        /// </summary>
        public long Deduped => Considered - _adds;
        /// <summary>
        /// Count of strings added to the pool, may be larger than <seealso cref="Count"/> if there is a maxCount
        /// </summary>
        public long Added => _adds;
        public long Evicted => _evicted;

        private long GetFirstUse() => _lastUse;
        private long GetMultipleUse() => _lastUse | 1;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="InternPool"/> class, 
        /// and is unbounded.
        /// </summary>
        public InternPool() 
        {
            _maxLength = int.MaxValue;

#if NET5_0 || NETCOREAPP3_1
            InternPoolEventSource.Log.IsEnabled();
#endif
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="InternPool"/> class that is empty, 
        /// but has reserved space for <paramref name="capacity"/> entries 
        /// and is unbounded.
        /// </summary>
        /// <param name="capacity">The initial size of the <see cref="InternPool"/></param>
        public InternPool(int capacity) : this()
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            if (capacity > 0)
            {
                Initialize(capacity);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternPool"/> class that is empty, 
        /// but has reserved space for <paramref name="capacity"/> items 
        /// and is capped at <paramref name="maxCount"/> entries, with items being evicted based on least recently used.
        /// </summary>
        /// <param name="capacity">The initial size of the <see cref="InternPool"/></param>
        /// <param name="maxCount">The max size of the <see cref="InternPool"/>; 
        /// least recently used entries are evicted when max size is reached and a new item is added.</param>
        public InternPool(int capacity, int maxCount) : this()
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }
            if (maxCount < -1 || maxCount == 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.maxSize);
            }
            if ((uint)capacity > (uint)maxCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            _maxCount = maxCount;
            if (capacity > 0)
            {
                Initialize(capacity);
            }
        }

        public InternPool(int capacity, int maxCount, int maxLength)
            : this(capacity, maxCount)
        {
            if (maxLength < 1)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.maxLength);
            }

            _maxLength = maxLength;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternPool"/> class, 
        /// contains deduplicated entries copied from the specified collection, 
        /// and is unbounded.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new set.</param>
        public InternPool(IEnumerable<string> collection) : this(collection, maxCount: -1)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InternPool"/> class, 
        /// contains deduplicated entries copied from the specified collection, 
        /// and is capped at <paramref name="maxCount"/> entries, with items being evicted based on least recently used.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new set.</param>
        /// <param name="maxCount">The max size of the <see cref="InternPool"/>; 
        /// least recently used entries are evicted when max size is reached and a new item is added.</param>
        public InternPool(IEnumerable<string> collection, int maxCount) : this()
        {
            if (collection == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.collection);
            }
            if (maxCount < -1 || maxCount == 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.maxSize);
            }

            _maxCount = maxCount;

            if (collection is InternPool otherAsInternPool)
            {
                ConstructFrom(otherAsInternPool);
            }
            else
            {
                // To avoid excess resizes, first set size based on collection's count. The collection may
                // contain duplicates, so call TrimExcess if resulting InternPool is larger than the threshold.
                if (collection is ICollection<string> coll)
                {
                    int count = coll.Count;
                    if (count > 0)
                    {
                        Initialize(count);
                    }
                }

                ((ISet<string>)this).UnionWith(collection);

                if (_count > 0 && _entries!.Length / _count > ShrinkThreshold)
                {
                    TrimExcess();
                }
            }
        }

        /// <summary>Adds the specified ASCII string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public string InternAscii(byte[] asciiValue)
            => InternAscii(new ReadOnlySpan<byte>(asciiValue));

        /// <summary>Adds the specified ASCII string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The element to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
#if !NETSTANDARD2_0
        public string InternAscii(ReadOnlySpan<byte> asciiValue)
        {
            if (asciiValue.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            if (asciiValue.Length > _maxLength)
            {
                return Encoding.ASCII.GetString(asciiValue);
            }

            char[]? array = null;
            if (asciiValue.Length > StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(asciiValue.Length);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[asciiValue.Length] : array;
#endif

            int count = Encoding.ASCII.GetChars(asciiValue, span);
            span = span.Slice(0, count);

            string value = Intern(span);

            if (array != null)
            {
                ArrayPool<char>.Shared.Return(array);
            }

            return value;
        }
#else
        public unsafe string InternAscii(ReadOnlySpan<byte> asciiValue)
        {
            if (asciiValue.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            fixed (byte* pValue = &MemoryMarshal.GetReference(asciiValue))
            {
                if (asciiValue.Length > _maxLength)
                {
                    return Encoding.ASCII.GetString(pValue, asciiValue.Length);
                }

                char[]? array = null;
                if (asciiValue.Length > StackAllocThresholdChars)
                {
                    array = ArrayPool<char>.Shared.Rent(asciiValue.Length);
                }

                Span<char> span = array is null ? stackalloc char[asciiValue.Length] : array;

                int count;
                fixed (char* pOutput = &MemoryMarshal.GetReference(span))
                {
                    count = Encoding.ASCII.GetChars(pValue, asciiValue.Length, pOutput, span.Length);
                }

                span = span.Slice(0, count);

                string value = Intern(span);

                if (array != null)
                {
                    ArrayPool<char>.Shared.Return(array);
                }

                return value;
            }
        }
#endif
        /// <summary>Adds the specified <paramref name="encoding"/> string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <param name="encoding">The specific encoding to use.</param>
        /// <returns>The interned string.</returns>
        public string Intern(byte[] value, Encoding encoding)
            => Intern(new ReadOnlySpan<byte>(value), encoding);

        /// <summary>Adds the specified <paramref name="encoding"/> string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <param name="encoding">The specific encoding to use.</param>
        /// <returns>The interned string.</returns>
#if !NETSTANDARD2_0
        public string Intern(ReadOnlySpan<byte> value, Encoding encoding)
        {
            if (value.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            if (encoding.GetMaxCharCount(value.Length) > _maxLength)
            {
                return encoding.GetString(value);
            }

            char[]? array = null;

            int count = encoding.GetCharCount(value);
            if (count > _maxLength)
            {
                return encoding.GetString(value);
            }

            if (count > StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(count);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[count] : array;
#endif

            count = encoding.GetChars(value, span);
            span = span.Slice(0, count);

            string strValue = Intern(span);

            if (array != null)
            {
                ArrayPool<char>.Shared.Return(array);
            }

            return strValue;
        }
#else
        public unsafe string Intern(ReadOnlySpan<byte> value, Encoding encoding)
        {
            if (value.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            fixed (byte* pValue = &MemoryMarshal.GetReference(value))
            {
                if (encoding.GetMaxCharCount(value.Length) > _maxLength)
                {
                    return encoding.GetString(pValue, value.Length);
                }

                char[]? array = null;

                int count = encoding.GetCharCount(pValue, value.Length);
                if (count > _maxLength)
                {
                    return encoding.GetString(pValue, value.Length);
                }

                if (count > StackAllocThresholdChars)
                {
                    array = ArrayPool<char>.Shared.Rent(count);
                }

                Span<char> span = array is null ? stackalloc char[count] : array;

                fixed (char* pOutput = &MemoryMarshal.GetReference(span))
                {
                    count = Encoding.ASCII.GetChars(pValue, value.Length, pOutput, span.Length);
                }
                span = span.Slice(0, count);

                string strValue = Intern(span);

                if (array != null)
                {
                    ArrayPool<char>.Shared.Return(array);
                }

                return strValue;
            }
        }
#endif

        /// <summary>Adds the specified UTF8 string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public string InternUtf8(byte[] utf8Value)
            => InternUtf8(new ReadOnlySpan<byte>(utf8Value));

        /// <summary>Adds the specified UTF8 string to the intern pool if it's not already contained.</summary>
        /// <param name="value">The byte sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
#if NET5_0 || NETCOREAPP3_1
        public string InternUtf8(ReadOnlySpan<byte> utf8Value)
        {
            if (utf8Value.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            int count = utf8Value.Length * 2; // 2 x length for invalid sequence encoding
            if (count > _maxLength)
            {
                return Encoding.UTF8.GetString(utf8Value);
            }

            char[]? array = null;

            if (count > StackAllocThresholdChars)
            {
                array = ArrayPool<char>.Shared.Rent(count);
            }

#if NET5_0
            Span<char> span = array is null ? stackalloc char[StackAllocThresholdChars] : array;
#else
            Span<char> span = array is null ? stackalloc char[count] : array;
#endif
            Utf8.ToUtf16(utf8Value, span, out _, out count);

            span = span.Slice(0, count);

            string value = Intern(span);

            if (array != null)
            {
                ArrayPool<char>.Shared.Return(array);
            }

            return value;
        }
#else
        public unsafe string InternUtf8(ReadOnlySpan<byte> utf8Value)
        {
            if (utf8Value.Length == 0)
            {
                _lastUse += 2;
                return string.Empty;
            }

            fixed (byte* pValue = &MemoryMarshal.GetReference(utf8Value))
            {
                if (utf8Value.Length * 2 > _maxLength)
                {
                    return Encoding.UTF8.GetString(pValue, utf8Value.Length);
                }

                char[]? array = null;

                int count = Encoding.UTF8.GetCharCount(pValue, utf8Value.Length);
                if (count > _maxLength)
                {
                    return Encoding.UTF8.GetString(pValue, utf8Value.Length);
                }

                if (count > StackAllocThresholdChars)
                {
                    array = ArrayPool<char>.Shared.Rent(count);
                }

                Span<char> span = array is null ? stackalloc char[count] : array;

                fixed (char* pOutput = &MemoryMarshal.GetReference(span))
                {
                    count = Encoding.ASCII.GetChars(pValue, utf8Value.Length, pOutput, span.Length);
                }
                span = span.Slice(0, count);

                string value = Intern(span);

                if (array != null)
                {
                    ArrayPool<char>.Shared.Return(array);
                }

                return value;
            }
        }
#endif
        internal int GetHashCode(ReadOnlySpan<char> value, out bool randomisedHash)
        {
            randomisedHash = _randomisedHash;
            return randomisedHash ? value.GetRandomizedHashCode() : value.GetNonRandomizedHashCode();
        }

        /// <summary>Adds the specified element to the intern pool if it's not already contained.</summary>
        /// <param name="value">The char sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        internal string Intern(int hashCode, bool randomisedHash, ReadOnlySpan<char> value)
        {
            _lastUse += 2;
            if (value.Length == 0) return string.Empty;
            if (value.Length > _maxLength) return value.ToString();

            if (_buckets == null)
            {
                Initialize(0);
            }
            Debug.Assert(_buckets != null);

            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "expected entries to be non-null");

            uint collisionCount = 0;
            ref int bucket = ref Unsafe.NullRef<int>();

            if (randomisedHash != _randomisedHash)
            {
                // Precalcuated hash mode changed before the lock was taken
                hashCode = _randomisedHash ? value.GetRandomizedHashCode() : value.GetNonRandomizedHashCode();
            }

            bucket = ref GetBucketRef(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based

            while (i >= 0)
            {
                ref Entry entry = ref entries[i];
                if (entry.HashCode == hashCode && value.SequenceEqual(entry.Value.AsSpan()))
                {
                    if (entry.LastUse < 0)
                    {
                        RemoveFromChurnPool(entry.Value, entry.LastUse);
                    }
                    entry.LastUse = GetMultipleUse();
                    return entry.Value;
                }
                i = entry.Next;

                collisionCount++;
                if (collisionCount > (uint)entries.Length)
                {
                    // The chain of entries forms a loop, which means a concurrent update has happened.
                    ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }
            }

            return AddNewEntry(value.ToString(), ref entries, hashCode, collisionCount, ref bucket);
        }

        /// <summary>Adds the specified element to the intern pool if it's not already contained.</summary>
        /// <param name="value">The char sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public string Intern(char[] value)
            => Intern(new ReadOnlySpan<char>(value));

        /// <summary>Adds the specified element to the intern pool if it's not already contained.</summary>
        /// <param name="value">The char sequence to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
        public string Intern(ReadOnlySpan<char> value)
        {
            _lastUse += 2;
            if (value.Length == 0) return string.Empty;
            if (value.Length > _maxLength) return value.ToString();

            if (_buckets == null)
            {
                Initialize(0);
            }
            Debug.Assert(_buckets != null);

            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "expected entries to be non-null");

            int hashCode;

            uint collisionCount = 0;
            ref int bucket = ref Unsafe.NullRef<int>();

            hashCode = _randomisedHash ? value.GetRandomizedHashCode() : value.GetNonRandomizedHashCode();
            bucket = ref GetBucketRef(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based

            while (i >= 0)
            {
                ref Entry entry = ref entries[i];
                if (entry.HashCode == hashCode && value.SequenceEqual(entry.Value.AsSpan()))
                {
                    if (entry.LastUse < 0)
                    {
                        RemoveFromChurnPool(entry.Value, entry.LastUse);
                    }
                    entry.LastUse = GetMultipleUse();
                    return entry.Value;
                }
                i = entry.Next;

                collisionCount++;
                if (collisionCount > (uint)entries.Length)
                {
                    // The chain of entries forms a loop, which means a concurrent update has happened.
                    ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }
            }

            return AddNewEntry(value.ToString(), ref entries, hashCode, collisionCount, ref bucket);
        }

        /// <summary>Adds the specified element to the intern pool if it's not already contained.</summary>
        /// <param name="value">The element to add to the intern pool.</param>
        /// <returns>The interned string.</returns>
#if !NETSTANDARD2_0
        [return: NotNullIfNotNull("value")]
#endif
        public string? Intern(string? value)
        {
            _lastUse += 2;
            if (value is null) return null;
            if (value.Length == 0) return string.Empty;
            if (value.Length > _maxLength) return value;

            if (_buckets == null)
            {
                Initialize(0);
            }
            Debug.Assert(_buckets != null);

            Entry[]? entries = _entries;
            Debug.Assert(entries != null, "expected entries to be non-null");

            int hashCode;

            uint collisionCount = 0;
            ref int bucket = ref Unsafe.NullRef<int>();

            hashCode = _randomisedHash ? value.GetRandomizedHashCode() : value.GetNonRandomizedHashCode();
            bucket = ref GetBucketRef(hashCode);
            int i = bucket - 1; // Value in _buckets is 1-based

            while (i >= 0)
            {
                ref Entry entry = ref entries[i];
                if (entry.HashCode == hashCode && entry.Value == value)
                {
                    if (entry.LastUse < 0)
                    {
                        RemoveFromChurnPool(entry.Value, entry.LastUse);
                    }
                    entry.LastUse = GetMultipleUse();
                    return entry.Value;
                }
                i = entry.Next;

                collisionCount++;
                if (collisionCount > (uint)entries.Length)
                {
                    // The chain of entries forms a loop, which means a concurrent update has happened.
                    ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                }
            }

            return AddNewEntry(value, ref entries, hashCode, collisionCount, ref bucket);
        }

        /// <summary>Initializes the InternPool from another InternPool with the same element type and equality comparer.</summary>
        private void ConstructFrom(InternPool source)
        {
            if (source.Count == 0)
            {
                // As well as short-circuiting on the rest of the work done,
                // this avoids errors from trying to access source._buckets
                // or source._entries when they aren't initialized.
                return;
            }

            int capacity = source._buckets!.Length;
            int threshold = HashHelpers.ExpandPrime(source.Count + 1);

            if (threshold >= capacity)
            {
                _buckets = (int[])source._buckets.Clone();
                _entries = (Entry[])source._entries!.Clone();
                _freeList = source._freeList;
                _freeCount = source._freeCount;
                _count = source._count;

                if (IntPtr.Size == 8)
                {
                    _fastModMultiplier = source._fastModMultiplier;
                }
            }
            else
            {
                Initialize(source.Count);

                Entry[]? entries = source._entries;
                for (int i = 0; i < source._count; i++)
                {
                    ref Entry entry = ref entries![i];
                    if (entry.Next >= -1)
                    {
                        Intern(entry.Value);
                    }
                }
            }

            Debug.Assert(Count == source.Count);
        }

        bool ISet<string>.Add(string item)
        {
            var value = Intern(item);
            return ReferenceEquals(value, item);
        }

        void ICollection<string>.Add(string item) => Intern(item);

        /// <summary>Removes all elements from the <see cref="InternPool"/> object.</summary>
        public void Clear()
        {
            int count = _count;
            if (count > 0)
            {
                Debug.Assert(_buckets != null, "_buckets should be non-null");
                Debug.Assert(_entries != null, "_entries should be non-null");

                Array.Clear(_buckets, 0, _buckets.Length);
                _count = 0;
                _freeList = -1;
                _freeCount = 0;
                Array.Clear(_entries, 0, count);
            }
        }

        /// <summary>Determines whether the <see cref="InternPool"/> contains the specified element.</summary>
        /// <param name="item">The element to locate in the <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object contains the specified element; otherwise, false.</returns>
        public bool Contains(string item)
        {
            if (item is null || item.Length == 0) return true;

            return FindItemIndex(item) >= 0;
        }

        /// <summary>Gets the index of the item in <see cref="_entries"/>, or -1 if it's not in the set.</summary>
        private int FindItemIndex(string item)
        {
            int[]? buckets = _buckets;
            if (buckets != null)
            {
                Entry[]? entries = _entries;
                Debug.Assert(entries != null, "Expected _entries to be initialized");

                uint collisionCount = 0;

                int hashCode = _randomisedHash ? item.GetRandomizedHashCode() : item.GetNonRandomizedHashCode();

                int i = GetBucketRef(hashCode) - 1; // Value in _buckets is 1-based
                while (i >= 0)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.HashCode == hashCode && entry.Value == item)
                    {
                        return i;
                    }
                    i = entry.Next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop, which means a concurrent update has happened.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }

            return -1;
        }

        /// <summary>Gets a reference to the specified hashcode's bucket, containing an index into <see cref="_entries"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref int GetBucketRef(int hashCode)
        {
            int[] buckets = _buckets!;
            if (IntPtr.Size == 8)
            {
                return ref buckets[HashHelpers.FastMod((uint)hashCode, (uint)buckets.Length, _fastModMultiplier)];
            }

            return ref buckets[(uint)hashCode % (uint)buckets.Length];
        }

        public bool Remove(string value)
            => Remove(value, isEviction: false);
        
        private bool Remove(string value, bool isEviction)
        {
            if (value is null || value.Length == 0) return false;

            if (_buckets != null)
            {
                Entry[]? entries = _entries;
                Debug.Assert(entries != null, "entries should be non-null");

                uint collisionCount = 0;
                int last = -1;
                int hashCode = _randomisedHash ? value.GetRandomizedHashCode() : value.GetNonRandomizedHashCode();

                ref int bucket = ref GetBucketRef(hashCode);
                int i = bucket - 1; // Value in buckets is 1-based

                while (i >= 0)
                {
                    ref Entry entry = ref entries[i];

                    if (entry.HashCode == hashCode && entry.Value == value)
                    {
                        if (last < 0)
                        {
                            bucket = entry.Next + 1; // Value in buckets is 1-based
                        }
                        else
                        {
                            entries[last].Next = entry.Next;
                        }

                        Debug.Assert((StartOfFreeList - _freeList) < 0, "shouldn't underflow because max hashtable length is MaxPrimeArrayLength = 0x7FEFFFFD(2146435069) _freelist underflow threshold 2147483646");
                        entry.Next = StartOfFreeList - _freeList;

                        if (!isEviction && entry.LastUse < 0)
                        {
                            RemoveFromChurnPool(entry.Value, entry.LastUse);
                        }
                        entry.Value = default!;

                        _freeList = i;
                        _freeCount++;
                        return true;
                    }

                    last = i;
                    i = entry.Next;

                    collisionCount++;
                    if (collisionCount > (uint)entries.Length)
                    {
                        // The chain of entries forms a loop; which means a concurrent update has happened.
                        // Break out of the loop and throw, rather than looping forever.
                        ThrowHelper.ThrowInvalidOperationException_ConcurrentOperationsNotSupported();
                    }
                }
            }

            return false;
        }

        bool ICollection<string>.IsReadOnly => false;

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<string> IEnumerable<string>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>Modifies the current <see cref="InternPool"/> object to contain all elements that are present in itself, the specified collection, or both.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        void ISet<string>.UnionWith(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            foreach (string item in other)
            {
                Intern(item);
            }
        }

        /// <summary>Modifies the current <see cref="InternPool"/> object to contain only elements that are present in that object and in the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        void ISet<string>.IntersectWith(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // Intersection of anything with empty set is empty set, so return if count is 0.
            // Same if the set intersecting with itself is the same set.
            if (Count == 0 || other == this)
            {
                return;
            }

            // If other is known to be empty, intersection is empty set; remove all elements, and we're done.
            if (other is ICollection<string> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }

                // Faster if other is a hashset using same equality comparer; so check
                // that other is a hashset using the same equality comparer.
                if (other is InternPool otherAsSet)
                {
                    IntersectWithInternPool(otherAsSet);
                    return;
                }
            }

            IntersectWithEnumerable(other);
        }

        /// <summary>Removes all elements in the specified collection from the current <see cref="InternPool"/> object.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        void ISet<string>.ExceptWith(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // This is already the empty set; return.
            if (Count == 0)
            {
                return;
            }

            // Special case if other is this; a set minus itself is the empty set.
            if (other == this)
            {
                Clear();
                return;
            }

            // Remove every element in other from this.
            foreach (string element in other)
            {
                Remove(element);
            }
        }

        /// <summary>Modifies the current <see cref="InternPool"/> object to contain only elements that are present either in that object or in the specified collection, but not both.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        void ISet<string>.SymmetricExceptWith(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // If set is empty, then symmetric difference is other.
            if (Count == 0)
            {
                ((ISet<string>)this).UnionWith(other);
                return;
            }

            // Special-case this; the symmetric difference of a set with itself is the empty set.
            if (other == this)
            {
                Clear();
                return;
            }

            // If other is a InternPool, it has unique elements according to its equality comparer,
            // but if they're using different equality comparers, then assumption of uniqueness
            // will fail. So first check if other is a hashset using the same equality comparer;
            // symmetric except is a lot faster and avoids bit array allocations if we can assume
            // uniqueness.
            if (other is InternPool otherAsSet)
            {
                SymmetricExceptWithUniqueInternPool(otherAsSet);
            }
            else
            {
                SymmetricExceptWithEnumerable(other);
            }
        }

        /// <summary>Determines whether a <see cref="InternPool"/> object is a subset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object is a subset of <paramref name="other"/>; otherwise, false.</returns>
        bool ISet<string>.IsSubsetOf(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // The empty set is a subset of any set, and a set is a subset of itself.
            // Set is always a subset of itself
            if (Count == 0 || other == this)
            {
                return true;
            }

            // Faster if other has unique elements according to this equality comparer; so check
            // that other is a hashset using the same equality comparer.
            if (other is InternPool otherAsSet)
            {
                // if this has more elements then it can't be a subset
                if (Count > otherAsSet.Count)
                {
                    return false;
                }

                // already checked that we're using same equality comparer. simply check that
                // each element in this is contained in other.
                return IsSubsetOfInternPool(otherAsSet);
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount >= 0;
        }

        /// <summary>Determines whether a <see cref="InternPool"/> object is a proper subset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object is a proper subset of <paramref name="other"/>; otherwise, false.</returns>
        bool ISet<string>.IsProperSubsetOf(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // No set is a proper subset of itself.
            if (other == this)
            {
                return false;
            }

            if (other is ICollection<string> otherAsCollection)
            {
                // No set is a proper subset of an empty set.
                if (otherAsCollection.Count == 0)
                {
                    return false;
                }

                // The empty set is a proper subset of anything but the empty set.
                if (Count == 0)
                {
                    return otherAsCollection.Count > 0;
                }

                // Faster if other is a hashset (and we're using same equality comparer).
                if (other is InternPool otherAsSet)
                {
                    if (Count >= otherAsSet.Count)
                    {
                        return false;
                    }

                    // This has strictly less than number of items in other, so the following
                    // check suffices for proper subset.
                    return IsSubsetOfInternPool(otherAsSet);
                }
            }

            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: false);
            return uniqueCount == Count && unfoundCount > 0;
        }

        /// <summary>Determines whether a <see cref="InternPool"/> object is a proper superset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object is a superset of <paramref name="other"/>; otherwise, false.</returns>
        bool ISet<string>.IsSupersetOf(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // A set is always a superset of itself.
            if (other == this)
            {
                return true;
            }

            // Try to fall out early based on counts.
            if (other is ICollection<string> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    return true;
                }

                // Try to compare based on counts alone if other is a hashset with same equality comparer.
                if (other is InternPool otherAsSet &&
                    otherAsSet.Count > Count)
                {
                    return false;
                }
            }

            return ContainsAllElements(other);
        }

        /// <summary>Determines whether a <see cref="InternPool"/> object is a proper superset of the specified collection.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object is a proper superset of <paramref name="other"/>; otherwise, false.</returns>
        bool ISet<string>.IsProperSupersetOf(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // The empty set isn't a proper superset of any set, and a set is never a strict superset of itself.
            if (Count == 0 || other == this)
            {
                return false;
            }

            if (other is ICollection<string> otherAsCollection)
            {
                // If other is the empty set then this is a superset.
                if (otherAsCollection.Count == 0)
                {
                    // Note that this has at least one element, based on above check.
                    return true;
                }

                // Faster if other is a hashset with the same equality comparer
                if (other is InternPool otherAsSet)
                {
                    if (otherAsSet.Count >= Count)
                    {
                        return false;
                    }

                    // Now perform element check.
                    return ContainsAllElements(otherAsSet);
                }
            }

            // Couldn't fall out in the above cases; do it the long way
            (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
            return uniqueCount < Count && unfoundCount == 0;
        }

        /// <summary>Determines whether the current <see cref="InternPool"/> object and a specified collection share common elements.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object and <paramref name="other"/> share at least one common element; otherwise, false.</returns>
        bool ISet<string>.Overlaps(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            if (Count == 0)
            {
                return false;
            }

            // Set overlaps itself
            if (other == this)
            {
                return true;
            }

            foreach (string element in other)
            {
                if (Contains(element))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Determines whether a <see cref="InternPool"/> object and the specified collection contain the same elements.</summary>
        /// <param name="other">The collection to compare to the current <see cref="InternPool"/> object.</param>
        /// <returns>true if the <see cref="InternPool"/> object is equal to <paramref name="other"/>; otherwise, false.</returns>
        bool ISet<string>.SetEquals(IEnumerable<string> other)
        {
            if (other == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.other);
            }

            // A set is equal to itself.
            if (other == this)
            {
                return true;
            }

            // Faster if other is a hashset and we're using same equality comparer.
            if (other is InternPool otherAsSet)
            {
                // Attempt to return early: since both contain unique elements, if they have
                // different counts, then they can't be equal.
                if (Count != otherAsSet.Count)
                {
                    return false;
                }

                // Already confirmed that the sets have the same number of distinct elements, so if
                // one is a superset of the other then they must be equal.
                return ContainsAllElements(otherAsSet);
            }
            else
            {
                // If this count is 0 but other contains at least one element, they can't be equal.
                if (Count == 0 &&
                    other is ICollection<string> otherAsCollection &&
                    otherAsCollection.Count > 0)
                {
                    return false;
                }

                (int uniqueCount, int unfoundCount) = CheckUniqueAndUnfoundElements(other, returnIfUnfound: true);
                return uniqueCount == Count && unfoundCount == 0;
            }
        }

        public void CopyTo(string[] array) => CopyTo(array, 0, Count);

        /// <summary>Copies the elements of a <see cref="InternPool"/> object to an array, starting at the specified array index.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
        public void CopyTo(string[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);

        public void CopyTo(string[] array, int arrayIndex, int count)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            // Check array index valid index into array.
            if (arrayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), arrayIndex, "Must be non negative number");
            }

            // Also throw if count less than 0.
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "Must be non negative number");
            }

            // Will the array, starting at arrayIndex, be able to hold elements? Note: not
            // checking arrayIndex >= array.Length (consistency with list of allowing
            // count of 0; subsequent check takes care of the rest)
            if (arrayIndex > array.Length || count > array.Length - arrayIndex)
            {
                ThrowHelper.ThrowArgumentException_ArrayPlusOffTooSmall();
            }

            Entry[]? entries = _entries;
            for (int i = 0; i < _count && count != 0; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    array[arrayIndex++] = entry.Value;
                    count--;
                }
            }
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate from a <see cref="InternPool"/> collection.</summary>
        public int RemoveWhere(Predicate<string> match)
        {
            if (match == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.match);
            }

            Entry[]? entries = _entries;
            int numRemoved = 0;
            for (int i = 0; i < _count; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    // Cache value in case delegate removes it
                    string value = entry.Value;
                    if (match(value))
                    {
                        // Check again that remove actually removed it.
                        if (Remove(value))
                        {
                            numRemoved++;
                        }
                    }
                }
            }

            return numRemoved;
        }

        /// <summary>Ensures that this hash set can hold the specified number of elements without growing.</summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.capacity);
            }

            int currentCapacity = _entries == null ? 0 : _entries.Length;
            if (currentCapacity >= capacity)
            {
                return currentCapacity;
            }

            if (_buckets == null)
            {
                return Initialize(capacity);
            }

            int newSize = HashHelpers.GetPrime(capacity);
            Resize(newSize, forceNewHashCodes: false);
            return newSize;
        }

        private void Resize() => Resize(HashHelpers.ExpandPrime(_count), forceNewHashCodes: false);

        private void Resize(int newSize, bool forceNewHashCodes)
        {
            Debug.Assert(_entries != null, "_entries should be non-null");
            Debug.Assert(newSize >= _entries.Length);

            var entries = new Entry[newSize];

            int count = _count;
            Array.Copy(_entries, entries, count);

            if (forceNewHashCodes)
            {
                _randomisedHash = true;

                for (int i = 0; i < count; i++)
                {
                    ref Entry entry = ref entries[i];
                    if (entry.Next >= -1)
                    {
                        entry.HashCode = entry.Value.GetRandomizedHashCode();
                    }
                }
            }

            // Assign member variables after both arrays allocated to guard against corruption from OOM if second fails
            _buckets = new int[newSize];
            if (IntPtr.Size == 8)
            {
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)newSize);
            }

            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next >= -1)
                {
                    ref int bucket = ref GetBucketRef(entry.HashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = i + 1;
                }
            }

            _entries = entries;
        }

        /// <summary>
        /// Sets the capacity of a <see cref="InternPool"/> object to the actual number of elements it contains,
        /// rounded up to a nearby, implementation-specific value.
        /// </summary>
        public void TrimExcess()
        {
            int capacity = Count;

            int newSize = HashHelpers.GetPrime(capacity);
            Entry[]? oldEntries = _entries;
            int currentCapacity = oldEntries == null ? 0 : oldEntries.Length;
            if (newSize >= currentCapacity)
            {
                return;
            }

            int oldCount = _count;
            _version++;
            Initialize(newSize);
            Entry[]? entries = _entries;
            int count = 0;
            for (int i = 0; i < oldCount; i++)
            {
                int hashCode = oldEntries![i].HashCode; // At this point, we know we have entries.
                if (oldEntries[i].Next >= -1)
                {
                    ref Entry entry = ref entries![count];
                    entry = oldEntries[i];
                    ref int bucket = ref GetBucketRef(hashCode);
                    entry.Next = bucket - 1; // Value in _buckets is 1-based
                    bucket = count + 1;
                    count++;
                }
            }

            _count = capacity;
            _freeCount = 0;
        }

        /// <summary>
        /// Initializes buckets and slots arrays. Uses suggested capacity by finding next prime
        /// greater than or equal to capacity.
        /// </summary>
        private int Initialize(int capacity)
        {
            int size = HashHelpers.GetPrime(capacity);
            var buckets = new int[size];
            var entries = new Entry[size];

            // Assign member variables after both arrays are allocated to guard against corruption from OOM if second fails.
            _freeList = -1;
            _buckets = buckets;
            _entries = entries;
            if (IntPtr.Size == 8)
            {
                _fastModMultiplier = HashHelpers.GetFastModMultiplier((uint)size);
            }

            return size;
        }

        private string AddNewEntry(string value, ref Entry[] entries, int hashCode, uint collisionCount, ref int bucket)
        {
            if ((uint)Count + 1 > (uint)_maxCount)
            {
                RemoveLeastRecentlyUsed();
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeCount--;
                Debug.Assert((StartOfFreeList - entries[_freeList].Next) >= -1, "shouldn't overflow because `next` cannot underflow");
                _freeList = StartOfFreeList - entries[_freeList].Next;
            }
            else
            {
                int count = _count;
                if (count == entries.Length)
                {
                    Resize();
                    Debug.Assert(_entries != null, "expected entries to be non-null");

                    entries = _entries;
                    bucket = ref GetBucketRef(hashCode);
                }
                index = count;
                _count = count + 1;
            }

            {
                ref Entry entry = ref entries[index];
                entry.HashCode = hashCode;
                entry.Next = bucket - 1; // Value in _buckets is 1-based
                entry.Value = value;
                entry.LastUse = GetFirstUse();
                bucket = index + 1;
                _version++;
            }

            if (collisionCount > HashHelpers.HashCollisionThreshold)
            {
                // If we hit the collision threshold we'll need to switch to randomized string hashing
                Resize(entries.Length, forceNewHashCodes: true);
            }

            _adds++;
            return value;
        }

        internal void Trim(TrimLevel trim)
        {
            var currentUse = _lastUse;

            long max0Distance;
            long max1Distance = long.MaxValue;

            switch (trim)
            {
                case TrimLevel.Minor:
                    max0Distance = (Count + (Count >> 1)) << 1;
                    break;
                case TrimLevel.Medium:
                    max0Distance = Count << 1;
                    max1Distance = (Count * 2) << 1;
                    break;
                default:
                    max0Distance = max1Distance = Count << 1;
                    break;
            }

            if (currentUse < max0Distance)
                return;

            _gen0Pool?.Clear();
            _gen1Pool?.Clear();
            Entry[]? entries = _entries;
            if (entries == null)
            {
                return;
            }

            int count = _count;
            var buckets = _buckets!;
            Array.Clear(buckets, 0, buckets.Length);

            var last = 0;
            var evicted = 0;

            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next < -1)
                {
                    continue;
                }

                var lastUse = entry.LastUse;
                bool discard = false;

                var distance = currentUse - lastUse;
                var gen = GetGeneration(lastUse);
                if (lastUse < 0)
                {
                    // Drop if already in churn pool
                    discard = true;
                }
                else if (gen == 0 && distance > max0Distance)
                {
                    discard = true;
                }
                else if (gen > 0 && distance > max1Distance)
                {
                    discard = true;
                }

                if (discard)
                {
                    entry.Value = null!;
                    evicted++;
                }
                else
                {
                    if (i != last) 
                    {
                        entries[last] = entry;
                        entry.Value = null!;
                        entry = ref entries[last];
                    }

                    ref int bucket = ref GetBucketRef(entry.HashCode);
                    // Value in _buckets is 1-based
                    entry.Next = bucket - 1;
                    bucket = last + 1;

                    last++;
                }
            }

            Debug.Assert(evicted == Count - last);

            _count = last;
            _evicted += evicted;
            _freeCount = 0;
        }

        private long _lastRemoved = 0;
        private bool _emptyGen1 = false;
        private void RemoveLeastRecentlyUsed()
        {
            List<(long lastUse, string value)> genPool = (_gen0Pool ??= new List<(long lastUse, string value)>(ChurnPoolSize));
            //List<(long lastUse, string value)> churnPool = (_gen0Pool ??= new List<(long lastUse, string value)>(ChurnPoolSize));

            Debug.Assert(Count != 0);

            if (genPool.Count == 0)
            {
                var gen1Pool = _gen1Pool;
                // See if we can remove a single item from the multiuse pool
                // before regenerating the single use pool.
                if (gen1Pool?.Count > 0 &&
                    (_emptyGen1 || gen1Pool[0].lastUse < _lastRemoved))
                {
                    genPool = gen1Pool;
                }
                else
                {
                    RegenerateChurnPool();
                    Debug.Assert(_gen0Pool.Count != 0 || _gen1Pool?.Count != 0);
                }
            }

            // Remove lowest
            if (genPool.Count == 0)
            {
                genPool = _gen1Pool!;
                // If we regnerated the pool and Gen0 is still empty,
                // empty Gen1 before trying to regenerate.
                _emptyGen1 = true;
            }
            string value;
            (_lastRemoved, value) = genPool[0];
            Remove(value, isEviction: true);
            genPool.RemoveAt(0);
            _evicted++;
        }

        private void RegenerateChurnPool()
        {
            _emptyGen1 = false;
            var gen0Pool = _gen0Pool!;
            var gen1Pool = (_gen1Pool ??= new List<(long lastUse, string value)>(ChurnPoolSize));

            Debug.Assert(gen0Pool.Count == 0);

            var entries = _entries!;

            long current0High = long.MinValue;
            long current0Low = long.MaxValue;

            long current1High = long.MinValue;
            long current1Low = long.MaxValue;

            if (gen1Pool.Count > 0)
            {
                current1Low = gen1Pool[0].lastUse;
                current1High = gen1Pool[gen1Pool.Count - 1].lastUse;
            }

            var count = _count;
            for (int i = 0; i < count; i++)
            {
                ref Entry entry = ref entries[i];
                if (entry.Next >= -1)
                {
                    var gen = GetGeneration(entry.LastUse);
                    if (gen > 0 && entry.LastUse < 0)
                    {
                        // Skip already in pool
                        continue;
                    }

                    ref long currentHigh = ref gen == 0 ? ref current0High : ref current1High;
                    ref long currentLow = ref gen == 0 ? ref current0Low : ref current1Low;
                    var genPool = gen == 0 ? gen0Pool : gen1Pool;

                    var lastUse = entry.LastUse;
                    Debug.Assert(lastUse > 0);

                    if (genPool.Count >= ChurnPoolSize &&
                        lastUse > currentHigh)
                        continue;

                    Debug.Assert(genPool.Count <= ChurnPoolSize);
                    if (genPool.Count >= ChurnPoolSize)
                    {
                        // Remove from end
                        genPool.RemoveAt(genPool.Count - 1);
                        currentHigh = genPool[genPool.Count - 1].lastUse;
                    }
                    Debug.Assert(genPool.Count < ChurnPoolSize);

                    if (lastUse > currentHigh)
                    {
                        // Insert at end
                        genPool.Add((lastUse, entry.Value));
                        currentHigh = lastUse;
                        if (lastUse < currentLow)
                        {
                            currentLow = lastUse;
                        }
                    }
                    else if (lastUse < currentLow)
                    {
                        // Insert at start
                        genPool.Insert(0, (lastUse, entry.Value));
                        currentLow = lastUse;
                    }
                    else
                    {
#if NET5_0
                        var span = CollectionsMarshal.AsSpan(genPool);
                        for (int index = 0; index < span.Length; index++)
                        {
                            (long use, _) = span[index];
#else
                        var length = genPool.Count;
                        for (int index = 0; index < length; index++)
                        {
                            (long use, _) = genPool[index];
#endif
                            if (use < lastUse) continue;

                            genPool.Insert(index, (lastUse, entry.Value));
                            break;
                        }

#if NET5_0
                        Debug.Assert(genPool!.Count > span.Length);
#else
                        Debug.Assert(genPool!.Count > length);
#endif
                        (currentHigh, _) = genPool[genPool.Count - 1];
                    }
                }
            }

#if NET5_0
            foreach ((long lastUse, string value) in CollectionsMarshal.AsSpan(gen0Pool))
            {
#else

            for (int i = 0; i < gen0Pool.Count; i++)
            {
                (long lastUse, string value) = gen0Pool[i];
#endif
                var index = FindItemIndex(value);
                Debug.Assert(index >= 0);


                ref Entry entry = ref entries[index];
                Debug.Assert(entry.LastUse == lastUse);

                // Negate lastuse to flag it as in the churn pool
                entry.LastUse = -entry.LastUse;
            }

#if NET5_0
            foreach ((long lastUse, string value) in CollectionsMarshal.AsSpan(gen1Pool))
            {
#else

            for (int i = 0; i < gen1Pool.Count; i++)
            {
                (long lastUse, string value) = gen1Pool[i];
#endif
                var index = FindItemIndex(value);
                Debug.Assert(index >= 0);


                ref Entry entry = ref entries[index];
                Debug.Assert(Math.Abs(entry.LastUse) == lastUse);

                if (entry.LastUse >= 0)
                {
                    // If new entry, negate LastUse to flag it as in the churn pool
                    entry.LastUse = -entry.LastUse;
                }
            }
        }

        private void RemoveFromChurnPool(string value, long lastUse)
        {
            int generation = GetGeneration(lastUse);
            Debug.Assert(lastUse < 0);

            var churnPool = GetPool(generation)!;

            var index = churnPool.BinarySearch((-lastUse, value));
            Debug.Assert(index >= 0);

            churnPool!.RemoveAt(index);
        }

        /// <summary>
        /// Checks if this contains of other's elements. Iterates over other's elements and
        /// returns false as soon as it finds an element in other that's not in this.
        /// Used by SupersetOf, ProperSupersetOf, and SetEquals.
        /// </summary>
        private bool ContainsAllElements(IEnumerable<string> other)
        {
            foreach (string element in other)
            {
                if (!Contains(element))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Implementation Notes:
        /// If other is a hashset and is using same equality comparer, then checking subset is
        /// faster. Simply check that each element in this is in other.
        ///
        /// Note: if other doesn't use same equality comparer, then Contains check is invalid,
        /// which is why callers must take are of this.
        ///
        /// If callers are concerned about whether this is a proper subset, they take care of that.
        /// </summary>
        private bool IsSubsetOfInternPool(InternPool other)
        {
            foreach (string item in this)
            {
                if (!other.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// If other is a hashset that uses same equality comparer, intersect is much faster
        /// because we can use other's Contains
        /// </summary>
        private void IntersectWithInternPool(InternPool other)
        {
            Entry[]? entries = _entries;
            for (int i = 0; i < _count; i++)
            {
                ref Entry entry = ref entries![i];
                if (entry.Next >= -1)
                {
                    string item = entry.Value;
                    if (!other.Contains(item))
                    {
                        Remove(item);
                    }
                }
            }
        }

        /// <summary>
        /// Iterate over other. If contained in this, mark an element in bit array corresponding to
        /// its position in _slots. If anything is unmarked (in bit array), remove it.
        ///
        /// This attempts to allocate on the stack, if below StackAllocThreshold.
        /// </summary>
        private unsafe void IntersectWithEnumerable(IEnumerable<string> other)
        {
            Debug.Assert(_buckets != null, "_buckets shouldn't be null; callers should check first");

            // Keep track of current last index; don't want to move past the end of our bit array
            // (could happen if another thread is modifying the collection).
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThresholdInts];
            BitHelper bitHelper = intArrayLength <= StackAllocThresholdInts ?
                new BitHelper(span.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            // Mark if contains: find index of in slots array and mark corresponding element in bit array.
            foreach (string item in other)
            {
                if (item is null || item.Length == 0) continue;

                int index = FindItemIndex(item);
                if (index >= 0)
                {
                    bitHelper.MarkBit(index);
                }
            }

            // If anything unmarked, remove it. Perf can be optimized here if BitHelper had a
            // FindFirstUnmarked method.
            for (int i = 0; i < originalCount; i++)
            {
                ref Entry entry = ref _entries![i];
                if (entry.Next >= -1 && !bitHelper.IsMarked(i))
                {
                    Remove(entry.Value);
                }
            }
        }

        /// <summary>
        /// if other is a set, we can assume it doesn't have duplicate elements, so use this
        /// technique: if can't remove, then it wasn't present in this set, so add.
        ///
        /// As with other methods, callers take care of ensuring that other is a hashset using the
        /// same equality comparer.
        /// </summary>
        /// <param name="other"></param>
        private void SymmetricExceptWithUniqueInternPool(InternPool other)
        {
            foreach (string item in other)
            {
                if (!Remove(item))
                {
                    Intern(item);
                }
            }
        }

        /// <summary>
        /// Implementation notes:
        ///
        /// Used for symmetric except when other isn't a InternPool. This is more tedious because
        /// other may contain duplicates. InternPool technique could fail in these situations:
        /// 1. Other has a duplicate that's not in this: InternPool technique would add then
        /// remove it.
        /// 2. Other has a duplicate that's in this: InternPool technique would remove then add it
        /// back.
        /// In general, its presence would be toggled each time it appears in other.
        ///
        /// This technique uses bit marking to indicate whether to add/remove the item. If already
        /// present in collection, it will get marked for deletion. If added from other, it will
        /// get marked as something not to remove.
        ///
        /// </summary>
        /// <param name="other"></param>
        private unsafe void SymmetricExceptWithEnumerable(IEnumerable<string> other)
        {
            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> itemsToRemoveSpan = stackalloc int[StackAllocThresholdInts / 2];
            BitHelper itemsToRemove = intArrayLength <= StackAllocThresholdInts / 2 ?
                new BitHelper(itemsToRemoveSpan.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            Span<int> itemsAddedFromOtherSpan = stackalloc int[StackAllocThresholdInts / 2];
            BitHelper itemsAddedFromOther = intArrayLength <= StackAllocThresholdInts / 2 ?
                new BitHelper(itemsAddedFromOtherSpan.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            foreach (string item in other)
            {
                if (item is null || item.Length == 0) continue;

                string value = Intern(item);
                int location = FindItemIndex(value);

                if (ReferenceEquals(value, item))
                {
                    // wasn't already present in collection; flag it as something not to remove
                    // *NOTE* if location is out of range, we should ignore. BitHelper will
                    // detect that it's out of bounds and not try to mark it. But it's
                    // expected that location could be out of bounds because adding the item
                    // will increase _lastIndex as soon as all the free spots are filled.
                    itemsAddedFromOther.MarkBit(location);
                }
                else
                {
                    // already there...if not added from other, mark for remove.
                    // *NOTE* Even though BitHelper will check that location is in range, we want
                    // to check here. There's no point in checking items beyond originalCount
                    // because they could not have been in the original collection
                    if (location < originalCount && !itemsAddedFromOther.IsMarked(location))
                    {
                        itemsToRemove.MarkBit(location);
                    }
                }
            }

            // if anything marked, remove it
            for (int i = 0; i < originalCount; i++)
            {
                if (itemsToRemove.IsMarked(i))
                {
                    Remove(_entries![i].Value);
                }
            }
        }

        /// <summary>
        /// Determines counts that can be used to determine equality, subset, and superset. This
        /// is only used when other is an IEnumerable and not a InternPool. If other is a InternPool
        /// these properties can be checked faster without use of marking because we can assume
        /// other has no duplicates.
        ///
        /// The following count checks are performed by callers:
        /// 1. Equals: checks if unfoundCount = 0 and uniqueFoundCount = _count; i.e. everything
        /// in other is in this and everything in this is in other
        /// 2. Subset: checks if unfoundCount >= 0 and uniqueFoundCount = _count; i.e. other may
        /// have elements not in this and everything in this is in other
        /// 3. Proper subset: checks if unfoundCount > 0 and uniqueFoundCount = _count; i.e
        /// other must have at least one element not in this and everything in this is in other
        /// 4. Proper superset: checks if unfound count = 0 and uniqueFoundCount strictly less
        /// than _count; i.e. everything in other was in this and this had at least one element
        /// not contained in other.
        ///
        /// An earlier implementation used delegates to perform these checks rather than returning
        /// an ElementCount struct; however this was changed due to the perf overhead of delegates.
        /// </summary>
        /// <param name="other"></param>
        /// <param name="returnIfUnfound">Allows us to finish faster for equals and proper superset
        /// because unfoundCount must be 0.</param>
        private unsafe (int UniqueCount, int UnfoundCount) CheckUniqueAndUnfoundElements(IEnumerable<string> other, bool returnIfUnfound)
        {
            // Need special case in case this has no elements.
            if (_count == 0)
            {
                int numElementsInOther = 0;
                foreach (string item in other)
                {
                    numElementsInOther++;
                    break; // break right away, all we want to know is whether other has 0 or 1 elements
                }

                return (UniqueCount: 0, UnfoundCount: numElementsInOther);
            }

            Debug.Assert((_buckets != null) && (_count > 0), "_buckets was null but count greater than 0");

            int originalCount = _count;
            int intArrayLength = BitHelper.ToIntArrayLength(originalCount);

            Span<int> span = stackalloc int[StackAllocThresholdInts];
            BitHelper bitHelper = intArrayLength <= StackAllocThresholdInts ?
                new BitHelper(span.Slice(0, intArrayLength), clear: true) :
                new BitHelper(new int[intArrayLength], clear: false);

            int unfoundCount = 0; // count of items in other not found in this
            int uniqueFoundCount = 0; // count of unique items in other found in this

            bool hasNull = false;
            bool hasEmpty = false;
            foreach (string item in other)
            {
                if (item is null)
                {
                    hasNull = true;
                    continue;
                }

                if (item.Length == 0)
                {
                    hasEmpty = true;
                    continue;
                }

                int index = FindItemIndex(item);
                if (index >= 0)
                {
                    if (!bitHelper.IsMarked(index))
                    {
                        // Item hasn't been seen yet.
                        bitHelper.MarkBit(index);
                        uniqueFoundCount++;
                    }
                }
                else
                {
                    unfoundCount++;
                    if (returnIfUnfound)
                    {
                        break;
                    }
                }
            }

            if (hasNull) uniqueFoundCount++;
            if (hasEmpty) uniqueFoundCount++;

            return (uniqueFoundCount, unfoundCount);
        }

#if NET5_0
        bool IReadOnlySet<string>.IsProperSubsetOf(IEnumerable<string> other)
            => ((ISet<string>)this).IsProperSubsetOf(other);

        bool IReadOnlySet<string>.IsProperSupersetOf(IEnumerable<string> other)
            => ((ISet<string>)this).IsProperSupersetOf(other);

        bool IReadOnlySet<string>.IsSubsetOf(IEnumerable<string> other)
            => ((ISet<string>)this).IsSubsetOf(other);

        bool IReadOnlySet<string>.IsSupersetOf(IEnumerable<string> other)
            => ((ISet<string>)this).IsSupersetOf(other);

        bool IReadOnlySet<string>.Overlaps(IEnumerable<string> other)
            => ((ISet<string>)this).Overlaps(other);

        bool IReadOnlySet<string>.SetEquals(IEnumerable<string> other)
            => ((ISet<string>)this).SetEquals(other);
#endif

        private struct Entry
        {
            public int HashCode;
            /// <summary>
            /// 0-based index of next entry in chain: -1 means end of chain
            /// also encodes whether this entry _itself_ is part of the free list by changing sign and subtracting 3,
            /// so -2 means end of free list, -3 means index 0 but on free list, -4 means index 1 but on free list, etc.
            /// </summary>
            public int Next;
            public string Value;
            public long LastUse;
        }

        internal enum TrimLevel
        {
            Minor = 0,
            Medium,
            Major,

            Max = Major
        }

        public struct Enumerator : IEnumerator<string>
        {
            private readonly InternPool _hashSet;
            private readonly int _version;
            private int _index;
            private string _current;

            internal Enumerator(InternPool hashSet)
            {
                _hashSet = hashSet;
                _version = hashSet._version;
                _index = 0;
                _current = default!;
            }

            public bool MoveNext()
            {
                if (_version != _hashSet._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
                // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
                while ((uint)_index < (uint)_hashSet._count)
                {
                    ref Entry entry = ref _hashSet._entries![_index++];
                    if (entry.Next >= -1)
                    {
                        _current = entry.Value;
                        return true;
                    }
                }

                _index = _hashSet._count + 1;
                _current = default!;
                return false;
            }

            public string Current => _current;

            public void Dispose() { }

            object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || (_index == _hashSet._count + 1))
                    {
                        ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();
                    }

                    return _current;
                }
            }

            void IEnumerator.Reset()
            {
                if (_version != _hashSet._version)
                {
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
                }

                _index = 0;
                _current = default!;
            }
        }
    }
}
