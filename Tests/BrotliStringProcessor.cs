using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tests
{
    public static class BrotliStringProcessor
    {
        private static readonly Encoding UTF8Encoding = Encoding.UTF8;
        private static readonly char[] AtoP = { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P' };

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        public static string CompressToAPString(string text)
        {
            byte[] compressedBytes = CompressToBytes(text);
            var result = new StringBuilder(compressedBytes.Length<<1);

            foreach (byte b in compressedBytes)
            {
                result.Append(AtoP[(b >> 4) & 0x0F]); // High nibble
                result.Append(AtoP[b & 0x0F]);          // Low nibble
            }

            return result.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.AggressiveInlining)]
        public static string DecompressFromAPString(string apString)
        {
            byte[] compressedBytes = new byte[apString.Length>>1];

            for (int i = 0; i < apString.Length; i += 2)
            {
                int highNibble = apString[i] - 'A';
                int lowNibble = apString[i + 1] - 'A';
                compressedBytes[i>>1] = (byte)((highNibble << 4) | lowNibble);
            }

            return DecompressFromBytes(compressedBytes);
        }

        private static byte[] CompressToBytes(string text)
        {
            int maxByteCount = UTF8Encoding.GetMaxByteCount(text.Length);
            byte[]? rentedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount);

            try
            {
                int actualByteCount = UTF8Encoding.GetBytes(text, 0, text.Length, rentedBytes, 0);
                int memoryStreamCapacity = actualByteCount >> 1;
                using var memoryStream = new MemoryStream(memoryStreamCapacity);
                using (var brotliStream = new BrotliStream(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    brotliStream.Write(rentedBytes, 0, actualByteCount);
                }
                return memoryStream.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }

        private static string DecompressFromBytes(byte[] compressedData)
        {
            using var memoryStream = new MemoryStream(compressedData.Length<<2);
            using (var brotliStream = new BrotliStream(new MemoryStream(compressedData), CompressionMode.Decompress))
            {
                const int bufferSize = 65536;
                brotliStream.CopyTo(memoryStream, bufferSize);
            }
            return UTF8Encoding.GetString(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
        }
    }
}
