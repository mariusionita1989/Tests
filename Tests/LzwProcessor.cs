using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tests
{
    public static class LzwProcessor
    {
        private const int InitialDictSize = 4;   // A, B, C, D
        private const int MaxDictSize = 16;      // 4-bit codes (0-15)
        private const int InitialBitLength = 2;  // Start with 2-bit codes
        private const int MaxBitLength = 4;      // Grow to 4-bit codes

        private readonly struct SpanKey : IEquatable<SpanKey>
        {
            private readonly string _source;
            private readonly int _start;
            private readonly int _length;

            public SpanKey(string source, int start, int length)
            {
                _source = source;
                _start = start;
                _length = length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public bool Equals(SpanKey other)
            {
                return _length == other._length &&
                       _source.AsSpan(_start, _length)
                       .SequenceEqual(other._source.AsSpan(other._start, other._length));
            }

            public override bool Equals(object? obj) => obj is SpanKey other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public override int GetHashCode()
            {
                var span = _source.AsSpan(_start, _length);
                var bytes = MemoryMarshal.AsBytes(span);
                unchecked
                {
                    int hash = 5381;
                    foreach (byte b in bytes)
                        hash = ((hash << 5) + hash) ^ b;
                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Compress(string input)
        {
            var dictionary = new Dictionary<SpanKey, int>(MaxDictSize);

            // Initialize with single character codes
            for (int i = 0; i < InitialDictSize; i++)
                dictionary[new SpanKey(((char)('A' + i)).ToString(), 0, 1)] = i;

            var codes = new List<int>(input.Length);
            int start = 0, length = 1;
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            while (start + length <= input.Length)
            {
                var candidate = new SpanKey(input, start, length);

                if (dictionary.ContainsKey(candidate))
                {
                    length++;
                    continue;
                }

                // Add previous match to output
                var previous = new SpanKey(input, start, length - 1);
                codes.Add(dictionary[previous]);

                // Handle dictionary growth or reset
                if (dictionary.Count < MaxDictSize - 1)
                {
                    dictionary[candidate] = dictionary.Count;
                    if (dictionary.Count > maxCode && currentBitLength < MaxBitLength)
                    {
                        currentBitLength++;
                        maxCode = (1 << currentBitLength) - 1;
                    }
                }
                else
                {
                    dictionary.Clear();
                    for (int i = 0; i < InitialDictSize; i++)
                        dictionary[new SpanKey(((char)('A' + i)).ToString(), 0, 1)] = i;
                    currentBitLength = InitialBitLength;
                    maxCode = (1 << currentBitLength) - 1;
                }

                start += length - 1;
                length = 1;
            }

            // Add last code
            if (length > 1)
                codes.Add(dictionary[new SpanKey(input, start, length - 1)]);

            return PackCodes(codes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static byte[] PackCodes(List<int> codes)
        {
            int totalBits = 0;
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            // Calculate total bits needed
            foreach (int code in codes)
            {
                totalBits += currentBitLength;
                if (code == maxCode && currentBitLength < MaxBitLength)
                {
                    currentBitLength++;
                    maxCode = (1 << currentBitLength) - 1;
                }
            }

            byte[] output = new byte[(totalBits + 7) / 8];
            int buffer = 0;
            int bitsInBuffer = 0;
            int outputPos = 0;
            currentBitLength = InitialBitLength;
            maxCode = (1 << currentBitLength) - 1;

            foreach (int code in codes)
            {
                int remainingBits = currentBitLength;
                int value = code;

                while (remainingBits > 0)
                {
                    int bitsToWrite = Math.Min(8 - bitsInBuffer, remainingBits);
                    int mask = (1 << bitsToWrite) - 1;
                    buffer |= (value & mask) << bitsInBuffer;
                    bitsInBuffer += bitsToWrite;
                    remainingBits -= bitsToWrite;
                    value >>= bitsToWrite;

                    if (bitsInBuffer == 8)
                    {
                        output[outputPos++] = (byte)buffer;
                        buffer = 0;
                        bitsInBuffer = 0;
                    }
                }

                if (code == maxCode && currentBitLength < MaxBitLength)
                {
                    currentBitLength++;
                    maxCode = (1 << currentBitLength) - 1;
                }
            }

            if (bitsInBuffer > 0)
                output[outputPos] = (byte)buffer;

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decompress(byte[] compressed)
        {
            if (compressed.Length == 0)
                return string.Empty;

            var codes = UnpackCodes(compressed);
            if (codes.Count == 0)
                return string.Empty;

            var dictionary = new Dictionary<int, string>(MaxDictSize);
            for (int i = 0; i < InitialDictSize; i++)
                dictionary[i] = ((char)('A' + i)).ToString();

            string previous = dictionary[codes[0]];
            var output = new StringBuilder(previous);
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            for (int i = 1; i < codes.Count; i++)
            {
                int code = codes[i];
                string entry;

                if (dictionary.TryGetValue(code, out entry))
                {
                    // Existing code
                }
                else if (code == dictionary.Count)
                {
                    // Special case for pattern growth
                    entry = previous + previous[0];
                }
                else
                {
                    throw new InvalidOperationException("Invalid LZW code");
                }

                output.Append(entry);

                // Add to dictionary if space available
                if (dictionary.Count < MaxDictSize - 1)
                {
                    dictionary[dictionary.Count] = previous + entry[0];
                    if (dictionary.Count > maxCode && currentBitLength < MaxBitLength)
                    {
                        currentBitLength++;
                        maxCode = (1 << currentBitLength) - 1;
                    }
                }
                else
                {
                    // Reset dictionary
                    dictionary.Clear();
                    for (int j = 0; j < InitialDictSize; j++)
                        dictionary[j] = ((char)('A' + j)).ToString();
                    currentBitLength = InitialBitLength;
                    maxCode = (1 << currentBitLength) - 1;
                }

                previous = entry;
            }

            return output.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static List<int> UnpackCodes(byte[] compressed)
        {
            var codes = new List<int>();
            int buffer = 0;
            int bitsInBuffer = 0;
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            foreach (byte b in compressed)
            {
                buffer |= b << bitsInBuffer;
                bitsInBuffer += 8;

                while (bitsInBuffer >= currentBitLength)
                {
                    int mask = (1 << currentBitLength) - 1;
                    int code = buffer & mask;
                    buffer >>= currentBitLength;
                    bitsInBuffer -= currentBitLength;
                    codes.Add(code);

                    if (code == maxCode && currentBitLength < MaxBitLength)
                    {
                        currentBitLength++;
                        maxCode = (1 << currentBitLength) - 1;
                    }
                }
            }

            return codes;
        }
    }
}
