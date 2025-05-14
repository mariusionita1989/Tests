using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Tests
{
    public static class RLE
    {
        private const int MaxChunk = 4;
        private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;
        private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Compress(ReadOnlySpan<char> input)
        {
            int length = input.Length;
            if (length == 0)
                return Array.Empty<byte>();

            // Worst case: every character is different (1 byte per char)
            int estimatedSize = length;
            byte[]? rented = estimatedSize > 2048 ? BytePool.Rent(estimatedSize) : null;
            Span<byte> buffer = rented ?? stackalloc byte[Math.Min(estimatedSize, 2048)];
            int pos = 0;

            ref char inputRef = ref MemoryMarshal.GetReference(input);
            char current = inputRef;
            int count = 1;

            // Process 4 characters at a time when possible
            int i = 1;
            for (; i <= length - 4; i += 4)
            {
                ProcessQuad(ref inputRef, i, ref current, ref count, ref buffer, ref rented, ref pos);
            }

            // Process remaining characters
            for (; i < length; i++)
            {
                char c = Unsafe.Add(ref inputRef, i);
                ProcessChar(c, ref current, ref count, ref buffer, ref rented, ref pos);
            }

            // Final run
            if (count > 0)
            {
                EnsureCapacity(ref buffer, ref rented, ref pos, 1);
                buffer[pos++] = PackCharCount(current, count);
            }

            byte[] result = buffer.Slice(0, pos).ToArray();
            if (rented != null) BytePool.Return(rented);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ProcessQuad(ref char inputRef, int i, ref char current, ref int count, ref Span<byte> buffer, ref byte[]? rented, ref int pos)
        {
            // Read 4 characters at once
            ref char quadRef = ref Unsafe.Add(ref inputRef, i);
            char c0 = quadRef;
            char c1 = Unsafe.Add(ref quadRef, 1);
            char c2 = Unsafe.Add(ref quadRef, 2);
            char c3 = Unsafe.Add(ref quadRef, 3);

            ProcessChar(c0, ref current, ref count, ref buffer, ref rented, ref pos);
            ProcessChar(c1, ref current, ref count, ref buffer, ref rented, ref pos);
            ProcessChar(c2, ref current, ref count, ref buffer, ref rented, ref pos);
            ProcessChar(c3, ref current, ref count, ref buffer, ref rented, ref pos);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ProcessChar(char c, ref char current, ref int count, ref Span<byte> buffer, ref byte[]? rented, ref int pos)
        {
            if (c == current)
            {
                if (++count == MaxChunk)
                {
                    EnsureCapacity(ref buffer, ref rented, ref pos, 1);
                    buffer[pos++] = PackCharCount(current, MaxChunk);
                    count = 0;
                }
            }
            else
            {
                if (count > 0)
                {
                    EnsureCapacity(ref buffer, ref rented, ref pos, 1);
                    buffer[pos++] = PackCharCount(current, count);
                }
                current = c;
                count = 1;
            }
        }

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decompress(ReadOnlySpan<byte> input)
        {
            if (input.Length == 0)
                return string.Empty;

            int maxLength = input.Length * MaxChunk;
            char[]? rented = maxLength > 2048 ? CharPool.Rent(maxLength) : null;
            Span<char> buffer = rented ?? stackalloc char[Math.Min(maxLength, 2048)];
            int pos = 0;

            ref byte inputRef = ref MemoryMarshal.GetReference(input);
            int length = input.Length;

            // Process 4 bytes at a time when possible
            int i = 0;
            for (; i <= length - 4; i += 4)
            {
                ProcessQuadDecompress(ref inputRef, i, ref buffer, ref rented, ref pos);
            }

            // Process remaining bytes
            for (; i < length; i++)
            {
                byte packed = Unsafe.Add(ref inputRef, i);
                (char c, int count) = UnpackCharCount(packed);
                EnsureCapacityDecompress(ref buffer, ref rented, ref pos, count);
                buffer.Slice(pos, count).Fill(c);
                pos += count;
            }

            string result = new string(buffer.Slice(0, pos));
            if (rented != null) CharPool.Return(rented);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void ProcessQuadDecompress(ref byte inputRef, int i, ref Span<char> buffer, ref char[]? rented, ref int pos)
        {
            byte p0 = Unsafe.Add(ref inputRef, i);
            byte p1 = Unsafe.Add(ref inputRef, i + 1);
            byte p2 = Unsafe.Add(ref inputRef, i + 2);
            byte p3 = Unsafe.Add(ref inputRef, i + 3);

            (char c0, int cnt0) = UnpackCharCount(p0);
            (char c1, int cnt1) = UnpackCharCount(p1);
            (char c2, int cnt2) = UnpackCharCount(p2);
            (char c3, int cnt3) = UnpackCharCount(p3);

            int total = cnt0 + cnt1 + cnt2 + cnt3;
            EnsureCapacityDecompress(ref buffer, ref rented, ref pos, total);

            buffer.Slice(pos, cnt0).Fill(c0);
            pos += cnt0;
            buffer.Slice(pos, cnt1).Fill(c1);
            pos += cnt1;
            buffer.Slice(pos, cnt2).Fill(c2);
            pos += cnt2;
            buffer.Slice(pos, cnt3).Fill(c3);
            pos += cnt3;
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
        private static void EnsureCapacityDecompress(ref Span<char> buffer, ref char[]? rented, ref int pos, int needed)
        {
            if (pos + needed > buffer.Length)
            {
                GrowBufferDecompress(ref buffer, ref rented, pos);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void GrowBuffer(ref Span<byte> buffer, ref byte[]? rented, int pos)
        {
            int newSize = buffer.Length << 1;
            byte[] newRented = BytePool.Rent(newSize);
            buffer.Slice(0, pos).CopyTo(newRented);

            if (rented != null)
                BytePool.Return(rented);

            rented = newRented;
            buffer = newRented;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static void GrowBufferDecompress(ref Span<char> buffer, ref char[]? rented, int pos)
        {
            int newSize = buffer.Length << 1;
            char[] newRented = CharPool.Rent(newSize);
            buffer.Slice(0, pos).CopyTo(newRented);

            if (rented != null)
                CharPool.Return(rented);

            rented = newRented;
            buffer = newRented;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static byte PackCharCount(char c, int count)
        {
            // Assumes characters are from 'A' to 'D' (2 bits for character, 2 bits for count)
            return (byte)(((c - 'A') << 2) | ((count - 1) & 0x03));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static (char c, int count) UnpackCharCount(byte packed)
        {
            // Extract character (upper 2 bits) and count (lower 2 bits)
            char c = (char)('A' + (packed >> 2));
            int count = (packed & 0x03) + 1;
            return (c, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe string ToSymbolString(ReadOnlySpan<byte> encoded)
        {
            int totalSymbols = encoded.Length << 2; // encoded.Length * 4
            string result = new string('\0', totalSymbols);

            fixed (char* pSymbols = result)
            fixed (byte* pEncoded = encoded)
            {
                char* pDest = pSymbols;
                byte* pSrc = pEncoded;
                byte* pEnd = pSrc + encoded.Length;

                const int charA = 'A'; // Base char for 00 → 'A'

                while (pSrc < pEnd)
                {
                    byte currentByte = *pSrc++;

                    // Extract all four 2-bit symbols in one pass (no branching, no bounds checks)
                    *pDest++ = (char)(charA + ((currentByte >> 6) & 0b11)); // Bits 7-6
                    *pDest++ = (char)(charA + ((currentByte >> 4) & 0b11)); // Bits 5-4
                    *pDest++ = (char)(charA + ((currentByte >> 2) & 0b11)); // Bits 3-2
                    *pDest++ = (char)(charA + (currentByte & 0b11));        // Bits 1-0
                }
            }

            return result;
        }
    }
}
