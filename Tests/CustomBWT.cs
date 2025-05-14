using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Text;
using Microsoft.Diagnostics.Runtime.Utilities;
using System.Numerics;

namespace Tests
{
    public static unsafe class CustomBWT
    {
        private const int StackAllocThreshold = 1024;
        private const byte AlphabetOffset = (byte)'A';
        private const int AlphabetSize = 4; // A=0, B=1, C=2, D=3
        private const int MaxAlphabetValue = AlphabetSize - 1;
        private const int IndexEncodingLength = 9; // 18 bits / 2 bits per char = 9 chars

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Compress(ReadOnlySpan<char> input)
        {
            int length = input.Length;
            if (length == 0) return string.Empty;

            byte[] doubleBytes = ArrayPool<byte>.Shared.Rent(length * 2);
            try
            {
                fixed (char* inputPtr = input)
                fixed (byte* bytesPtr = doubleBytes)
                {
                    // Convert input to bytes (A=0, B=1, C=2, D=3)
                    ConvertToBytes(inputPtr, bytesPtr, length);

                    // Create rotations by duplicating the input
                    Unsafe.CopyBlockUnaligned(bytesPtr + length, bytesPtr, (uint)length);

                    // Sort rotations
                    int[] indices = GC.AllocateUninitializedArray<int>(length);
                    for (int i = 0; i < length; i++)
                        indices[i] = i;

                    Array.Sort(indices, new RotationComparer(bytesPtr, length));

                    // Build transformed output
                    char[] transformed = GC.AllocateUninitializedArray<char>(length + IndexEncodingLength);
                    int originalIndex = BuildTransformedString(bytesPtr, indices, transformed.AsSpan(IndexEncodingLength), length);

                    // Encode the index in the first 9 characters
                    EncodeIndex(originalIndex, transformed.AsSpan(0, IndexEncodingLength));

                    return new string(transformed);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(doubleBytes);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decompress(string transformed)
        {
            int length = transformed.Length - IndexEncodingLength;
            if (length <= 0) return string.Empty;

            // Decode the index from the first 9 characters
            int index = DecodeIndex(transformed.AsSpan(0, IndexEncodingLength));
            if (index < 0 || index >= length)
                throw new ArgumentOutOfRangeException(nameof(transformed), "Invalid index encoded in transformed string");

            byte[] bytes = ArrayPool<byte>.Shared.Rent(length);
            int[] nextArray = length <= StackAllocThreshold ?
                stackalloc int[length].ToArray() :
                ArrayPool<int>.Shared.Rent(length);

            try
            {
                fixed (char* trPtr = transformed)
                fixed (byte* bytesPtr = bytes)
                fixed (int* nextPtr = nextArray)
                {
                    // Convert to bytes and validate (skip the first 9 chars which are the encoded index)
                    if (!ConvertAndValidate(trPtr + IndexEncodingLength, bytesPtr, length))
                        throw new ArgumentException("Input contains invalid characters (only A-D allowed)");

                    // Count frequencies with bounds checking
                    Span<int> counts = stackalloc int[AlphabetSize + 2]; // Extra slot for safety
                    counts.Clear();
                    for (int i = 0; i < length; i++)
                    {
                        byte val = bytesPtr[i];
                        if (val <= MaxAlphabetValue)
                            counts[val + 1]++;
                    }

                    // Compute prefix sums with bounds checking
                    ComputePrefixSums(counts);

                    // Build next array with strict bounds checking
                    BuildNextArraySafe(bytesPtr, nextPtr, counts, length);

                    // Reconstruct original string with bounds checking
                    char[] result = GC.AllocateUninitializedArray<char>(length);
                    ReconstructStringSafe(bytesPtr, nextPtr, result, index, length);

                    return new string(result);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bytes);
                if (nextArray.Length > StackAllocThreshold)
                    ArrayPool<int>.Shared.Return(nextArray);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void EncodeIndex(int index, Span<char> output)
        {
            for (int i = 0; i < IndexEncodingLength; i++)
            {
                output[i] = (char)((index & 0x3) + AlphabetOffset); // Get 2 least significant bits
                index >>= 2; // Shift right by 2 bits for next iteration
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static int DecodeIndex(ReadOnlySpan<char> input)
        {
            int index = 0;
            for (int i = IndexEncodingLength - 1; i >= 0; i--)
            {
                byte val = (byte)(input[i] - AlphabetOffset);
                if (val > MaxAlphabetValue) return -1; // Invalid encoding
                index = (index << 2) | val;
            }
            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe void ConvertToBytes(char* source, byte* dest, int length)
        {
            if (Avx2.IsSupported && length >= 32)
            {
                Vector256<byte> aVector = Vector256.Create(AlphabetOffset);
                int i = 0;
                for (; i <= length - 32; i += 32)
                {
                    Vector256<short> chars1 = Avx.LoadVector256((short*)(source + i));
                    Vector256<short> chars2 = Avx.LoadVector256((short*)(source + i + 16));
                    Vector256<byte> packed = Avx2.PackUnsignedSaturate(chars1, chars2);
                    Vector256<byte> normalized = Avx2.Subtract(packed, aVector);
                    Avx.Store(dest + i, Avx2.Permute4x64(normalized.AsInt64(), 0b11011000).AsByte());
                }
                for (; i < length; i++)
                    dest[i] = (byte)(source[i] - AlphabetOffset);
            }
            else
            {
                for (int i = 0; i < length; i++)
                    dest[i] = (byte)(source[i] - AlphabetOffset);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe bool ConvertAndValidate(char* source, byte* dest, int length)
        {
            bool isValid = true;

            if (Avx2.IsSupported && length >= 32)
            {
                Vector256<byte> aVector = Vector256.Create(AlphabetOffset);
                Vector256<byte> maxVector = Vector256.Create((byte)MaxAlphabetValue);
                int i = 0;

                for (; i <= length - 32; i += 32)
                {
                    Vector256<short> chars1 = Avx.LoadVector256((short*)(source + i));
                    Vector256<short> chars2 = Avx.LoadVector256((short*)(source + i + 16));
                    Vector256<byte> packed = Avx2.PackUnsignedSaturate(chars1, chars2);
                    Vector256<byte> normalized = Avx2.Subtract(packed, aVector);

                    // Check for invalid characters
                    Vector256<byte> invalid = Avx2.CompareGreaterThan(normalized.AsInt16(), maxVector.AsInt16()).AsByte();
                    if (!Avx.TestZ(invalid, invalid))
                        isValid = false;

                    Avx.Store(dest + i, Avx2.Permute4x64(normalized.AsInt64(), 0b11011000).AsByte());
                }

                for (; i < length; i++)
                {
                    byte val = (byte)(source[i] - AlphabetOffset);
                    if (val > MaxAlphabetValue) isValid = false;
                    dest[i] = val;
                }
            }
            else
            {
                for (int i = 0; i < length; i++)
                {
                    byte val = (byte)(source[i] - AlphabetOffset);
                    if (val > MaxAlphabetValue) isValid = false;
                    dest[i] = val;
                }
            }

            return isValid;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe int BuildTransformedString(byte* data, int[] indices, Span<char> output, int length)
        {
            int originalIndex = -1;
            int lastPos = length - 1;

            for (int i = 0; i < length; i++)
            {
                int start = indices[i];
                output[i] = (char)(data[start + lastPos] + AlphabetOffset);
                if (start == 0) originalIndex = i;
            }

            return originalIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ComputePrefixSums(Span<int> counts)
        {
            for (int i = 1; i <= AlphabetSize + 1; i++)
                counts[i] += counts[i - 1];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe void BuildNextArraySafe(byte* data, int* next, Span<int> counts, int length)
        {
            Span<int> currentPos = stackalloc int[AlphabetSize + 1];
            for (int i = 0; i <= MaxAlphabetValue; i++)
                currentPos[i] = counts[i];

            for (int i = 0; i < length; i++)
            {
                byte val = data[i];
                if (val <= MaxAlphabetValue)
                {
                    int pos = currentPos[val];
                    if (pos < length)
                    {
                        next[pos] = i;
                        currentPos[val] = pos + 1;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe void ReconstructStringSafe(byte* bytes, int* next, char[] result, int startIndex, int length)
        {
            if (startIndex < 0 || startIndex >= length)
                return;

            int current = next[startIndex];
            for (int i = 0; i < length; i++)
            {
                if (current < 0 || current >= length)
                    break;

                result[i] = (char)(bytes[current] + AlphabetOffset);
                current = next[current];
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
                byte* a = _data + x;
                byte* b = _data + y;
                int i = 0;

                if (Avx2.IsSupported && _length >= 32)
                {
                    for (; i <= _length - 32; i += 32)
                    {
                        Vector256<byte> vecA = Avx.LoadVector256(a + i);
                        Vector256<byte> vecB = Avx.LoadVector256(b + i);
                        uint mask = (uint)Avx2.MoveMask(Avx2.CompareEqual(vecA, vecB));
                        if (mask != 0xFFFFFFFF)
                        {
                            int diffPos = BitOperations.TrailingZeroCount(~mask);
                            return a[i + diffPos] - b[i + diffPos];
                        }
                    }
                }

                for (; i < _length; i++)
                {
                    int cmp = a[i] - b[i];
                    if (cmp != 0) return cmp;
                }
                return 0;
            }
        }
    }
}
