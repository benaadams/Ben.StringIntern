// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Numerics;
using System.Runtime.InteropServices;

namespace System.Collections.Generic
{
    internal static class OrdinalStringExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe static int GetNonRandomizedHashCode(this string str)
            => str.AsSpan().GetNonRandomizedHashCode();

        internal unsafe static int GetNonRandomizedHashCode(this ReadOnlySpan<char> value)
        {
            ref char currentChar = ref MemoryMarshal.GetReference(value);
            ref uint currentUint = ref Unsafe.As<char, uint>(ref currentChar);

            uint hash1 = (5381 << 16) + 5381;
            uint hash2 = hash1;

            int length = value.Length;

            while (length >= 4)
            {
                length -= 4;

                hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ currentUint;
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ Unsafe.AddByteOffset(ref currentUint, (IntPtr)1);

                currentUint = Unsafe.AddByteOffset(ref currentUint, (IntPtr)(sizeof(uint) * 2));
            }

            if (length >= 2)
            {
                length -= 2;

                hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ currentUint;

                currentChar = ref Unsafe.As<uint, char>(ref Unsafe.AddByteOffset(ref currentUint, (IntPtr)sizeof(uint)));
            }

            if (length > 0)
            {
                uint val = (uint)currentChar;
                hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ val;
            }

            return (int)(hash1 + (hash2 * 1566083941));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRandomizedHashCode(this string str)
            => GetRandomizedHashCode(str.AsSpan());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetRandomizedHashCode(this ReadOnlySpan<char> value)
        {
            ulong seed = Marvin.DefaultSeed;

            // Multiplication below will not overflow since going from positive Int32 to UInt32.
            return Marvin.ComputeHash32(ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(value)), (uint)value.Length * 2 /* in bytes, not chars */, (uint)seed, (uint)(seed >> 32));
        }
    }
}
