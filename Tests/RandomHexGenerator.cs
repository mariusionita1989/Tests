using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Tests
{
    public static class RandomHexGenerator
    {
        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private static ReadOnlySpan<char> HexChars => ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'];

        [SkipLocalsInit]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        public static string Generate(int length = 32)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
            int byteCount = (length + 1) >> 1;
            byte[]? heapBuffer = null;
            Span<byte> bytes = byteCount <= 512 ? stackalloc byte[byteCount] : (heapBuffer = new byte[byteCount]);
            _rng.GetBytes(bytes);
            string result = BytesToHexString(bytes, length);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static unsafe string BytesToHexString(ReadOnlySpan<byte> bytes, int finalLength)
        {
            fixed (char* hexCharsPtr = &MemoryMarshal.GetReference(HexChars))
            fixed (byte* bytesPtr = &MemoryMarshal.GetReference(bytes))
            {
                return string.Create(finalLength, ((IntPtr)hexCharsPtr, (IntPtr)bytesPtr, bytes.Length), static (span, state) =>
                {
                    var (hexCharsPtr, bytesPtr, bytesLength) = state;
                    var hexChars = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef<char>((char*)hexCharsPtr), 16);
                    byte* currentBytePtr = (byte*)bytesPtr;

                    for (int i = 0; i < span.Length;)
                    {
                        if (i >= span.Length) break;
                        byte b = *currentBytePtr++;
                        span[i++] = hexChars[(b >> 4) & 0x0F];
                        if (i >= span.Length) break;
                        span[i++] = hexChars[b & 0x0F];

                        if ((currentBytePtr - (byte*)bytesPtr) >= bytesLength) break;
                    }
                });
            }
        }
    }
}
