using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Tests
{
    public static class CustomizedRandomHexGenerator
    {
        private const int StackAllocThreshold = 512;
        private const string HexChars = "ABCDEFGHIJKLMNOP"; // 16 chars for 4-bit lookup
        private static readonly char[] HexCharsArray = HexChars.ToCharArray();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Generate(int length)
        {
            if (length <= 0) return string.Empty;

            // Stack allocation for small sizes
            if (length <= StackAllocThreshold)
            {
                Span<byte> buffer = stackalloc byte[length];
                RandomNumberGenerator.Fill(buffer);
                return ConvertToHexUnsafe(buffer);
            }

            // Array pool for larger sizes
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                RandomNumberGenerator.Fill(rentedArray.AsSpan(0, length));
                return ConvertToHexUnsafe(rentedArray.AsSpan(0, length));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedArray);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static unsafe string ConvertToHexUnsafe(Span<byte> inputBytes)
        {
            fixed (char* hexCharsPtr = HexCharsArray)
            fixed (byte* inputPtr = inputBytes)
            {
                return string.Create(inputBytes.Length, (Ptr: (IntPtr)hexCharsPtr, InputPtr: (IntPtr)inputPtr), (chars, state) =>
                {
                    char* hexChars = (char*)state.Ptr;
                    byte* input = (byte*)state.InputPtr;
                    int length = chars.Length;

                    // Process 8 bytes at a time to maximize throughput
                    int i = 0;
                    int lastBlockIndex = length - 8;

                    for (; i <= lastBlockIndex; i += 8)
                    {
                        chars[i] = hexChars[input[i] & 0x0F];
                        chars[i + 1] = hexChars[input[i + 1] & 0x0F];
                        chars[i + 2] = hexChars[input[i + 2] & 0x0F];
                        chars[i + 3] = hexChars[input[i + 3] & 0x0F];
                        chars[i + 4] = hexChars[input[i + 4] & 0x0F];
                        chars[i + 5] = hexChars[input[i + 5] & 0x0F];
                        chars[i + 6] = hexChars[input[i + 6] & 0x0F];
                        chars[i + 7] = hexChars[input[i + 7] & 0x0F];
                    }

                    // Process remaining bytes (0-7)
                    for (; i < length; i++)
                    {
                        chars[i] = hexChars[input[i] & 0x0F];
                    }
                });
            }
        }
    }
}
