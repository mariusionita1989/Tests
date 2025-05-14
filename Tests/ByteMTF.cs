using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Tests
{
    [SkipLocalsInit]
    public static class ByteMTF
    {
        private const int AlphabetSize = 256;
        private const int HeaderSize = 4;
        private const int MaxInputLength = 256 * 1024;
        private const int BatchSize = 1024; // Processing batch size for better cache locality
        private const int PrefetchDistance = 64; // Prefetch distance for better cache utilization

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe byte FindIndexAvx2(byte* alphabet, byte c)
        {
            Vector256<byte> target = Vector256.Create(c);

            // Process 64 bytes at a time (2x unrolled)
            for (int i = 0; i < AlphabetSize; i += 64)
            {
                // Prefetch next cache line
                if (i + 128 < AlphabetSize)
                {
                    Avx.Prefetch0(alphabet + i + 64);
                    Avx.Prefetch0(alphabet + i + 128);
                }

                var vec1 = Avx.LoadVector256(alphabet + i);
                var vec2 = Avx.LoadVector256(alphabet + i + 32);
                var cmp1 = Avx2.CompareEqual(vec1, target);
                var cmp2 = Avx2.CompareEqual(vec2, target);
                int mask1 = Avx2.MoveMask(cmp1);
                int mask2 = Avx2.MoveMask(cmp2);

                if (mask1 != 0)
                    return (byte)(i + BitOperations.TrailingZeroCount(mask1));
                if (mask2 != 0)
                    return (byte)(i + 32 + BitOperations.TrailingZeroCount(mask2));
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe byte FindIndexScalar(byte* alphabet, byte c)
        {
            // Fast path for common case (first 32 bytes)
            for (int i = 0; i < 32; i += 8)
            {
                if (alphabet[i] == c) return (byte)i;
                if (alphabet[i + 1] == c) return (byte)(i + 1);
                if (alphabet[i + 2] == c) return (byte)(i + 2);
                if (alphabet[i + 3] == c) return (byte)(i + 3);
                if (alphabet[i + 4] == c) return (byte)(i + 4);
                if (alphabet[i + 5] == c) return (byte)(i + 5);
                if (alphabet[i + 6] == c) return (byte)(i + 6);
                if (alphabet[i + 7] == c) return (byte)(i + 7);
            }

            // Process remaining bytes
            for (int i = 32; i < AlphabetSize; i += 8)
            {
                if (alphabet[i] == c) return (byte)i;
                if (alphabet[i + 1] == c) return (byte)(i + 1);
                if (alphabet[i + 2] == c) return (byte)(i + 2);
                if (alphabet[i + 3] == c) return (byte)(i + 3);
                if (alphabet[i + 4] == c) return (byte)(i + 4);
                if (alphabet[i + 5] == c) return (byte)(i + 5);
                if (alphabet[i + 6] == c) return (byte)(i + 6);
                if (alphabet[i + 7] == c) return (byte)(i + 7);
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe void MoveToFront(byte* alphabet, byte index)
        {
            if (index == 0) return;

            // Fast path for small indices
            if (index <= 8)
            {
                byte tempValue = alphabet[index];
                for (int i = index; i > 0; i--)
                {
                    alphabet[i] = alphabet[i - 1];
                }
                alphabet[0] = tempValue;
                return;
            }

            // Use memmove for larger indices
            byte value = alphabet[index];
            Buffer.MemoryCopy(alphabet, alphabet + 1, index, index);
            alphabet[0] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe void ProcessBatch(byte* inputPtr, byte* outputPtr, int batchSize, byte* alphabet)
        {
            byte* batchEnd = inputPtr + batchSize;
            byte* prefetchPtr = inputPtr + PrefetchDistance;

            while (inputPtr < batchEnd)
            {
                // Prefetch next batch of input data
                if (prefetchPtr < batchEnd)
                {
                    Avx.Prefetch0(prefetchPtr);
                    prefetchPtr += PrefetchDistance;
                }

                byte c = *inputPtr++;
                byte index = Avx2.IsSupported
                    ? FindIndexAvx2(alphabet, c)
                    : FindIndexScalar(alphabet, c);
                *outputPtr++ = index;
                MoveToFront(alphabet, index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe byte[] Encode(ReadOnlySpan<byte> input)
        {
            int length = input.Length;
            if (length > MaxInputLength)
                throw new ArgumentException($"Input too large (max {MaxInputLength})");

            byte[] output = GC.AllocateUninitializedArray<byte>(HeaderSize + length);
            BinaryPrimitives.WriteInt32BigEndian(output, length);

            // Align alphabet to 32-byte boundary for better SIMD performance
            byte* alphabet = stackalloc byte[AlphabetSize + 32];
            alphabet = (byte*)(((nuint)alphabet + 31) & ~(nuint)31);
            for (int i = 0; i < AlphabetSize; i++)
                alphabet[i] = (byte)i;

            fixed (byte* inPtr = input)
            fixed (byte* outPtr = output)
            {
                byte* inputPtr = inPtr;
                byte* outputPtr = outPtr + HeaderSize;
                byte* endPtr = inputPtr + length;

                // Process in batches for better cache locality
                while (inputPtr + BatchSize <= endPtr)
                {
                    ProcessBatch(inputPtr, outputPtr, BatchSize, alphabet);
                    inputPtr += BatchSize;
                    outputPtr += BatchSize;
                }

                // Process remaining elements
                if (inputPtr < endPtr)
                {
                    ProcessBatch(inputPtr, outputPtr, (int)(endPtr - inputPtr), alphabet);
                }
            }

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe byte[] Decode(ReadOnlySpan<byte> input)
        {
            if (input.Length < HeaderSize)
                throw new ArgumentException("Missing header");

            int length = BinaryPrimitives.ReadInt32BigEndian(input);
            if (length > MaxInputLength)
                throw new ArgumentException("Input too large");

            byte[] output = GC.AllocateUninitializedArray<byte>(length);

            // Align alphabet to 32-byte boundary for better SIMD performance
            byte* alphabet = stackalloc byte[AlphabetSize + 32];
            alphabet = (byte*)(((nuint)alphabet + 31) & ~(nuint)31);
            for (int i = 0; i < AlphabetSize; i++)
                alphabet[i] = (byte)i;

            fixed (byte* inPtr = input)
            fixed (byte* outPtr = output)
            {
                byte* inputPtr = inPtr + HeaderSize;
                byte* outputPtr = outPtr;
                byte* endPtr = outputPtr + length;
                byte* prefetchPtr = inputPtr + PrefetchDistance;

                // Process in batches for better cache locality
                while (outputPtr + BatchSize <= endPtr)
                {
                    byte* batchEnd = outputPtr + BatchSize;
                    while (outputPtr < batchEnd)
                    {
                        // Prefetch next batch of input data
                        if (prefetchPtr < inPtr + input.Length)
                        {
                            Avx.Prefetch0(prefetchPtr);
                            prefetchPtr += PrefetchDistance;
                        }

                        byte index = *inputPtr++;
                        byte value = alphabet[index];
                        *outputPtr++ = value;

                        if (index != 0)
                        {
                            MoveToFront(alphabet, index);
                        }
                    }
                }

                // Process remaining elements
                while (outputPtr < endPtr)
                {
                    byte index = *inputPtr++;
                    byte value = alphabet[index];
                    *outputPtr++ = value;

                    if (index != 0)
                    {
                        MoveToFront(alphabet, index);
                    }
                }
            }

            return output;
        }
    }
}
