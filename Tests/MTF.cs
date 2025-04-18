using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tests
{
    public static class MTF
    {
        private const int MaxChunk = 16;
        private const int MaxChunkMask = MaxChunk - 1;
        private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static byte[] Compress(ReadOnlySpan<char> input)
        {
            int length = input.Length;
            if (length == 0)
                return Array.Empty<byte>();

            int estimatedSize = (length >> 1) + 4;
            byte[]? rented = length <= 1024 ? null : Pool.Rent(estimatedSize);
            Span<byte> buffer = rented ?? stackalloc byte[estimatedSize <= 2048 ? estimatedSize : 2048];

            ref char inputRef = ref MemoryMarshal.GetReference(input);
            int pos = 0;
            char current = inputRef;
            int count = 1;

            for (int i = 1; i < length; ++i)
            {
                char c = Unsafe.Add(ref inputRef, i);

                if (c == current)
                {
                    if (count == MaxChunk)
                    {
                        buffer[pos++] = PackCharCount(current, MaxChunk);
                        count = 0;
                    }
                }
                else
                {
                    if (count > 0)
                        buffer[pos++] = PackCharCount(current, count);
                    current = c;
                    count = 1;
                }
            }

            // Final run
            if (count > 0)
                buffer[pos++] = PackCharCount(current, count);

            byte[] result = new byte[pos];
            buffer.Slice(0, pos).CopyTo(result);

            // Return rented array to pool if it was rented
            if (rented != null)
                Pool.Return(rented);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte PackCharCount(char c, int count)
        {
            // Assumes characters are from 'A' to 'P'
            return (byte)(((c - 'A') << 4) | ((count - 1) & 0x0F));
        }
    }
}
