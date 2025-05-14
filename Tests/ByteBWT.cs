using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Tests
{
    public static unsafe class ByteBWT
    {
        private const int StackAllocThreshold = 1024;
        private const int IndexEncodingBytes = 3;
        private const int Base = 256;
        private const int Kilo = 1024;
        private const int MaxInputLength = Base * Kilo;
        private const int SimdAlignment = 32;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Compress(ReadOnlySpan<byte> input)
        {
            int length = input.Length;
            if (length == 0) return Array.Empty<byte>();
            if (length > MaxInputLength)
                throw new ArgumentException($"Input exceeds maximum length of {MaxInputLength} bytes");

            // Use aligned memory for SIMD operations
            byte[] doubleBytes = GC.AllocateUninitializedArray<byte>((length << 1) + SimdAlignment, pinned: true);
            byte* alignedPtr = (byte*)(((nint)Unsafe.AsPointer(ref doubleBytes[0]) + SimdAlignment - 1) & ~(nint)(SimdAlignment - 1));

            try
            {
                Span<byte> byteSpan = new Span<byte>(alignedPtr, length << 1);
                input.CopyTo(byteSpan.Slice(0, length));
                input.CopyTo(byteSpan.Slice(length, length));

                int[] indices = GC.AllocateUninitializedArray<int>(length);
                for (int i = 0; i < length; i++)
                    indices[i] = i;

                Array.Sort(indices, new RotationComparer(alignedPtr, length));

                byte[] output = GC.AllocateUninitializedArray<byte>(length + IndexEncodingBytes);
                int originalIndex = BuildTransformedBytes(byteSpan, indices, output.AsSpan(IndexEncodingBytes), length);

                // Fixed header encoding
                output[0] = (byte)(originalIndex >> 16);
                output[1] = (byte)(originalIndex >> 8);
                output[2] = (byte)originalIndex;

                return output;
            }
            finally
            {
                // No need to return to pool since we used GC.AllocateUninitializedArray
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Decompress(ReadOnlySpan<byte> compressed)
        {
            int length = compressed.Length - IndexEncodingBytes;
            if (length <= 0) return Array.Empty<byte>();

            // Fixed index decoding
            int index = (compressed[0] << 16) | (compressed[1] << 8) | compressed[2];

            if (index < 0 || index >= length)
                throw new ArgumentOutOfRangeException(nameof(compressed), "Invalid index encoding");

            byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
            compressed.Slice(IndexEncodingBytes).CopyTo(bytes.AsSpan(0, length));

            Span<int> nextArray = length <= StackAllocThreshold ?
                stackalloc int[length] :
                new int[length];

            try
            {
                Span<int> counts = stackalloc int[Base];
                counts.Clear();

                // Count frequencies with SIMD if available
                if (Avx2.IsSupported && length >= Vector256<byte>.Count)
                {
                    CountBytesAvx2(bytes.AsSpan(0, length), counts);
                }
                else
                {
                    foreach (byte b in bytes.AsSpan(0, length))
                        counts[b]++;
                }

                // Prefix sum
                int sum = 0;
                for (int i = 0; i < Base; i++)
                {
                    int temp = counts[i];
                    counts[i] = sum;
                    sum += temp;
                }

                // Build next array
                for (int i = 0; i < length; i++)
                {
                    byte val = bytes[i];
                    nextArray[counts[val]++] = i;
                }

                byte[] result = GC.AllocateUninitializedArray<byte>(length);
                int current = nextArray[index];
                for (int i = 0; i < length; i++)
                {
                    result[i] = bytes[current];
                    current = nextArray[current];
                }

                return result;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static int BuildTransformedBytes(Span<byte> data, int[] indices, Span<byte> output, int length)
        {
            int originalIndex = -1;
            int lastPos = length - 1;

            ref byte dataRef = ref MemoryMarshal.GetReference(data);
            ref byte outputRef = ref MemoryMarshal.GetReference(output);
            ref int indicesRef = ref MemoryMarshal.GetArrayDataReference(indices);

            for (int i = 0; i < length; i++)
            {
                int start = Unsafe.Add(ref indicesRef, i);
                Unsafe.Add(ref outputRef, i) = Unsafe.Add(ref dataRef, start + lastPos);
                if (start == 0)
                    originalIndex = i;
            }

            return originalIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void CountBytesAvx2(Span<byte> input, Span<int> counts)
        {
            ref byte inputRef = ref MemoryMarshal.GetReference(input);
            ref int countsRef = ref MemoryMarshal.GetReference(counts);
            int length = input.Length;
            int vectorLength = length - (length % Vector256<byte>.Count);

            // Create 32 vectors where each vector has the index in its lanes
            Vector256<byte>[] indexVectors = new Vector256<byte>[256];
            for (int i = 0; i < 256; i++)
            {
                indexVectors[i] = Vector256.Create((byte)i);
            }

            Vector256<int>[] countVectors = new Vector256<int>[256];
            for (int i = 0; i < 256; i++)
            {
                countVectors[i] = Vector256<int>.Zero;
            }

            // Process in chunks
            for (int i = 0; i < vectorLength; i += Vector256<byte>.Count)
            {
                Vector256<byte> data = Unsafe.ReadUnaligned<Vector256<byte>>(ref Unsafe.Add(ref inputRef, i));

                for (int j = 0; j < 256; j++)
                {
                    Vector256<byte> mask = Vector256.Equals(data, indexVectors[j]);
                    uint moveMask = (uint)Avx2.MoveMask(mask);
                    // Convert the bitmask to counts
                    Vector256<int> increment = Vector256.Create(
                        BitOperations.PopCount(moveMask & 0xFF),
                        BitOperations.PopCount((moveMask >> 8) & 0xFF),
                        BitOperations.PopCount((moveMask >> 16) & 0xFF),
                        BitOperations.PopCount((moveMask >> 24) & 0xFF),
                        0, 0, 0, 0
                    );
                    countVectors[j] = Avx2.Add(countVectors[j], increment);
                }
            }

            // Horizontal sum
            for (int i = 0; i < 256; i++)
            {
                int sum = 0;
                for (int j = 0; j < Vector256<int>.Count; j++)
                {
                    sum += countVectors[i].GetElement(j);
                }
                Unsafe.Add(ref countsRef, i) += sum;
            }

            // Process remaining elements
            for (int i = vectorLength; i < length; i++)
            {
                byte b = Unsafe.Add(ref inputRef, i);
                Unsafe.Add(ref countsRef, b)++;
            }
        }

        private readonly unsafe struct RotationComparer : IComparer<int>
        {
            private readonly byte* _data;
            private readonly int _length;

            public RotationComparer(byte* data, int length)
            {
                _data = data;
                _length = length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public int Compare(int x, int y)
            {
                if (x == y) return 0;

                if (Avx2.IsSupported)
                {
                    return CompareAvx2(_data + x, _data + y, _length);
                }
                else if (Sse2.IsSupported)
                {
                    return CompareSse2(_data + x, _data + y, _length);
                }
                else
                {
                    return CompareFallback(_data + x, _data + y, _length);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            private static int CompareAvx2(byte* ptr1, byte* ptr2, int length)
            {
                byte* end = ptr1 + length;
                byte* endSimd = ptr1 + (length & ~(Vector256<byte>.Count - 1));

                while (ptr1 < endSimd)
                {
                    Vector256<byte> vec1 = Avx.LoadVector256(ptr1);
                    Vector256<byte> vec2 = Avx.LoadVector256(ptr2);

                    Vector256<byte> cmp = Avx2.CompareEqual(vec1, vec2);
                    uint mask = (uint)Avx2.MoveMask(cmp);

                    if (mask != 0xFFFFFFFF) // Compare with uint instead of 64-bit constant
                    {
                        // Find first differing byte
                        int diffPos = BitOperations.TrailingZeroCount(~mask);
                        return ptr1[diffPos].CompareTo(ptr2[diffPos]);
                    }

                    ptr1 += Vector256<byte>.Count;
                    ptr2 += Vector256<byte>.Count;
                }

                // Handle remaining bytes
                while (ptr1 < end)
                {
                    int cmp = *ptr1 - *ptr2;
                    if (cmp != 0) return cmp;
                    ptr1++;
                    ptr2++;
                }

                return 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            private static int CompareSse2(byte* ptr1, byte* ptr2, int length)
            {
                byte* end = ptr1 + length;
                byte* endSimd = ptr1 + (length & ~(Vector128<byte>.Count - 1));

                while (ptr1 < endSimd)
                {
                    Vector128<byte> vec1 = Sse2.LoadVector128(ptr1);
                    Vector128<byte> vec2 = Sse2.LoadVector128(ptr2);

                    Vector128<byte> cmp = Sse2.CompareEqual(vec1, vec2);
                    int mask = Sse2.MoveMask(cmp);

                    if (mask != 0xFFFF) // If not all equal
                    {
                        // Find first differing byte
                        int diffPos = BitOperations.TrailingZeroCount((ushort)~mask);
                        return ptr1[diffPos].CompareTo(ptr2[diffPos]);
                    }

                    ptr1 += Vector128<byte>.Count;
                    ptr2 += Vector128<byte>.Count;
                }

                // Handle remaining bytes
                while (ptr1 < end)
                {
                    int cmp = *ptr1 - *ptr2;
                    if (cmp != 0) return cmp;
                    ptr1++;
                    ptr2++;
                }

                return 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            private static int CompareFallback(byte* ptr1, byte* ptr2, int length)
            {
                for (int i = 0; i < length; i++)
                {
                    int cmp = ptr1[i] - ptr2[i];
                    if (cmp != 0) return cmp;
                }
                return 0;
            }
        }
    }
}
