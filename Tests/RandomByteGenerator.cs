using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Tests
{
    public static class RandomByteGenerator
    {
        private const int StackAllocThreshold = 512; // Increased from 256
        private const int NoCopyThreshold = 4096; // New threshold for avoiding final copy

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Generate(int length)
        {
            if (length <= 0)
                return Array.Empty<byte>();

            // For small arrays, use stack allocation
            if (length <= StackAllocThreshold)
            {
                return GenerateWithStackAlloc(length);
            }

            // For very large arrays, use a special no-copy path
            if (length >= NoCopyThreshold)
            {
                return GenerateLargeNoCopy(length);
            }

            // Medium-sized arrays use array pool with copy
            return GenerateWithArrayPool(length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GenerateWithStackAlloc(int length)
        {
            Span<byte> buffer = stackalloc byte[length];
            RandomNumberGenerator.Fill(buffer);
            return buffer.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GenerateWithArrayPool(int length)
        {
            byte[] array = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                RandomNumberGenerator.Fill(new Span<byte>(array, 0, length));
                return array.AsSpan(0, length).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte[] GenerateLargeNoCopy(int length)
        {
            byte[] result = GC.AllocateUninitializedArray<byte>(length);
            RandomNumberGenerator.Fill(result);
            return result;
        }
    }
}
