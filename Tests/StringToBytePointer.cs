using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Tests
{
    public static class StringToByteSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static void ConvertStringToSpan(string input, out Span<ulong> ulongs, out Span<int> indexes)
        {
            if (string.IsNullOrEmpty(input))
                throw new ArgumentException("Input string cannot be null or empty.");

            int length = input.Length;
            int ulongLength = (length + 31) >> 5; // Each 32 chars => 1 block
            int doubleUlongLength = ulongLength << 1;

            var ulongArray = GC.AllocateUninitializedArray<ulong>(doubleUlongLength);
            var indexArray = GC.AllocateUninitializedArray<int>(length);

            ref ulong ulongPtr = ref MemoryMarshal.GetArrayDataReference(ulongArray);
            ref int indexPtr = ref MemoryMarshal.GetArrayDataReference(indexArray);
            ref char inputPtr = ref MemoryMarshal.GetReference(input.AsSpan());

            int i = 0;
            int ulongIndex = 0;
            while (i < length)
            {
                ulong value = 0UL;
                int remaining = length - i;
                int limit = remaining >= 32 ? 32 : remaining;

                int j = 0;
                // Fully unroll by 8
                for (; j <= limit - 8; j += 8)
                {
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 0) - 'A') << ((j + 0) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 1) - 'A') << ((j + 1) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 2) - 'A') << ((j + 2) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 3) - 'A') << ((j + 3) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 4) - 'A') << ((j + 4) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 5) - 'A') << ((j + 5) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 6) - 'A') << ((j + 6) << 1);
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j + 7) - 'A') << ((j + 7) << 1);
                }

                // Handle leftover (0-7 remaining)
                for (; j < limit; j++)
                {
                    value |= (ulong)(Unsafe.Add(ref inputPtr, i + j) - 'A') << (j << 1);
                }

                Unsafe.Add(ref ulongPtr, ulongIndex++) = value;
                i += 32;
            }

            // Duplicate ulongs
            for (int k = 0; k < (doubleUlongLength >> 1); k++)
            {
                Unsafe.Add(ref ulongPtr, ulongIndex++) = Unsafe.Add(ref ulongPtr, k);
            }

            // Fill indexes
            for (int idx = 0; idx < length; idx++)
            {
                Unsafe.Add(ref indexPtr, idx) = idx;
            }

            ulongs = ulongArray.AsSpan();
            indexes = indexArray.AsSpan();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe void ConvertStringToSpanSIMD(string input, out Span<byte> bytes, out Span<int> indexes)
        {
            // Assume input is never null or empty (as per your requirements)
            int length = input.Length;
            int doubleLength = length << 1;

            // Rent memory from the ArrayPool
            var byteArray = ArrayPool<byte>.Shared.Rent(doubleLength);
            var indexArray = ArrayPool<int>.Shared.Rent(length);

            try
            {
                fixed (char* charPtr = input)
                fixed (byte* bytePtr = byteArray)
                fixed (int* indexPtr = indexArray)
                {
                    int i = 0;

                    // Process 64 characters at a time using AVX2
                    if (Avx2.IsSupported && length >= 32)
                    {
                        // Since we know characters are A-D, subtracting 'A' gives us 0-3
                        // We can process 64 characters at a time (32 chars per 256-bit vector)
                        var subtractVector = Vector256.Create((short)'A');
                        for (; i <= length - 32; i += 32)
                        {
                            // Load 32 characters (16-bit shorts)
                            var v0 = Avx2.LoadVector256((short*)(charPtr + i));
                            var v1 = Avx2.LoadVector256((short*)(charPtr + i + 16));

                            // Subtract 'A' (0-3 result)
                            v0 = Avx2.Subtract(v0, subtractVector);
                            v1 = Avx2.Subtract(v1, subtractVector);

                            // Pack to 8-bit (unsigned saturation is fine since we know values are 0-3)
                            var packed = Avx2.PackUnsignedSaturate(v0, v1);

                            // Permute to proper order
                            var permuted = Avx2.Permute4x64(packed.AsUInt64(), 0b11011000).AsByte();

                            // Store 32 bytes
                            Avx2.Store(bytePtr + i, permuted);
                        }
                    }

                    // Process 16 characters at a time using SSE2
                    if (Sse2.IsSupported && length >= 16)
                    {
                        var subtractVector = Vector128.Create((short)'A');
                        for (; i <= length - 16; i += 16)
                        {
                            var v0 = Sse2.LoadVector128((short*)(charPtr + i));
                            var v1 = Sse2.LoadVector128((short*)(charPtr + i + 8));

                            v0 = Sse2.Subtract(v0, subtractVector);
                            v1 = Sse2.Subtract(v1, subtractVector);

                            var packed = Sse2.PackUnsignedSaturate(v0, v1);
                            Sse2.Store(bytePtr + i, packed);
                        }
                    }

                    // Process remaining characters (scalar)
                    for (; i < length; i++)
                    {
                        bytePtr[i] = (byte)(charPtr[i] - 'A');
                    }

                    // Fill indexes using optimized method
                    FillIndexes(indexPtr, length);
                }

                bytes = new Span<byte>(byteArray, 0, length);
                indexes = new Span<int>(indexArray, 0, length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(byteArray);
                ArrayPool<int>.Shared.Return(indexArray);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static unsafe void FillIndexes(int* indexPtr, int length)
        {
            // Use AVX2 to fill 8 indices at a time
            if (Avx2.IsSupported && length >= 8)
            {
                int i = 0;
                var baseIndices = Vector256.Create(0, 1, 2, 3, 4, 5, 6, 7);
                var increment = Vector256.Create(8);

                for (; i <= length - 8; i += 8)
                {
                    var indices = Avx2.Add(baseIndices, Vector256.Create(i));
                    Avx2.Store(indexPtr + i, indices);
                }

                // Handle remaining with SSE2 if needed
                if (Sse2.IsSupported && i <= length - 4)
                {
                    var base4 = Vector128.Create(0, 1, 2, 3);
                    for (; i <= length - 4; i += 4)
                    {
                        var indices = Sse2.Add(base4, Vector128.Create(i));
                        Sse2.Store(indexPtr + i, indices);
                    }
                }

                // Scalar remainder
                for (; i < length; i++)
                {
                    indexPtr[i] = i;
                }
            }
            else if (Sse2.IsSupported && length >= 4)
            {
                int i = 0;
                var base4 = Vector128.Create(0, 1, 2, 3);
                for (; i <= length - 4; i += 4)
                {
                    var indices = Sse2.Add(base4, Vector128.Create(i));
                    Sse2.Store(indexPtr + i, indices);
                }

                // Scalar remainder
                for (; i < length; i++)
                {
                    indexPtr[i] = i;
                }
            }
            else
            {
                // Pure scalar
                for (int i = 0; i < length; i++)
                {
                    indexPtr[i] = i;
                }
            }
        }
    }
}
