using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tests
{
    public static class ByteRLE
    {
        private const int MaxChunk = 256; // Using full byte for count (1-256)
        private const int BufferThreshold = 2048; // Threshold for renting from pool
        private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Compress(ReadOnlySpan<byte> input)
        {
            int length = input.Length;
            if (length == 0)
                return Array.Empty<byte>();

            // Worst case: every byte is different (2 bytes per value)
            int estimatedSize = length << 1; // length * 2
            byte[]? rented = estimatedSize > BufferThreshold ? BytePool.Rent(estimatedSize) : null;
            Span<byte> buffer = rented ?? stackalloc byte[Math.Min(estimatedSize, BufferThreshold)];
            int pos = 0;

            ref byte inputRef = ref MemoryMarshal.GetReference(input);
            byte current = inputRef;
            int count = 1;

            int i = 1;
            // Process 4 bytes at a time when possible
            for (; i <= length - 4; i += 4)
            {
                // Read 4 bytes at once
                ref byte quadRef = ref Unsafe.Add(ref inputRef, i);
                byte b0 = quadRef;
                byte b1 = Unsafe.Add(ref quadRef, 1);
                byte b2 = Unsafe.Add(ref quadRef, 2);
                byte b3 = Unsafe.Add(ref quadRef, 3);

                // Process each byte in the quad
                ProcessByteInline(b0, ref current, ref count, ref buffer, ref rented, ref pos);
                ProcessByteInline(b1, ref current, ref count, ref buffer, ref rented, ref pos);
                ProcessByteInline(b2, ref current, ref count, ref buffer, ref rented, ref pos);
                ProcessByteInline(b3, ref current, ref count, ref buffer, ref rented, ref pos);
            }

            // Process remaining bytes
            for (; i < length; i++)
            {
                byte b = Unsafe.Add(ref inputRef, i);
                ProcessByteInline(b, ref current, ref count, ref buffer, ref rented, ref pos);
            }

            // Final run
            if (count > 0)
            {
                EnsureCapacity(ref buffer, ref rented, ref pos, 2);
                buffer[pos++] = current;
                buffer[pos++] = (byte)(count - 1); // Store count-1 to get full 0-255 range
            }

            byte[] result = buffer.Slice(0, pos).ToArray();
            if (rented != null) BytePool.Return(rented);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ProcessByteInline(byte b, ref byte current, ref int count, ref Span<byte> buffer, ref byte[]? rented, ref int pos)
        {
            if (b == current)
            {
                if (++count == MaxChunk)
                {
                    EnsureCapacity(ref buffer, ref rented, ref pos, 2);
                    buffer[pos++] = current;
                    buffer[pos++] = (byte)(MaxChunk - 1);
                    count = 0;
                }
            }
            else
            {
                if (count > 0)
                {
                    EnsureCapacity(ref buffer, ref rented, ref pos, 2);
                    buffer[pos++] = current;
                    buffer[pos++] = (byte)(count - 1);
                }
                current = b;
                count = 1;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Decompress(ReadOnlySpan<byte> input)
        {
            if (input.Length == 0)
                return Array.Empty<byte>();

            // Calculate maximum possible output size (each pair could represent up to MaxChunk bytes)
            int maxLength = (input.Length >> 1) * MaxChunk; // (input.Length / 2) * MaxChunk
            byte[]? rented = maxLength > BufferThreshold ? BytePool.Rent(maxLength) : null;
            Span<byte> buffer = rented ?? stackalloc byte[Math.Min(maxLength, BufferThreshold)];
            int pos = 0;

            ref byte inputRef = ref MemoryMarshal.GetReference(input);
            int length = input.Length;

            // Process in pairs (value + count)
            for (int i = 0; i < length;)
            {
                byte value = Unsafe.Add(ref inputRef, i++);
                byte countByte = Unsafe.Add(ref inputRef, i++);
                int count = countByte + 1; // Restore original count (1-256)

                EnsureCapacity(ref buffer, ref rented, ref pos, count);
                buffer.Slice(pos, count).Fill(value);
                pos += count;
            }

            byte[] result = buffer.Slice(0, pos).ToArray();
            if (rented != null) BytePool.Return(rented);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void EnsureCapacity(ref Span<byte> buffer, ref byte[]? rented, ref int pos, int needed)
        {
            if (pos + needed > buffer.Length)
            {
                GrowBuffer(ref buffer, ref rented, pos);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void GrowBuffer(ref Span<byte> buffer, ref byte[]? rented, int pos)
        {
            int newSize = buffer.Length << 1; // buffer.Length * 2
            byte[] newRented = BytePool.Rent(newSize);
            buffer.Slice(0, pos).CopyTo(newRented);

            if (rented != null)
                BytePool.Return(rented);

            rented = newRented;
            buffer = newRented;
        }
    }
}
