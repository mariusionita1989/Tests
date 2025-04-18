using System.Buffers;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tests
{
    public static class RangeMapper
    {
        private const int AlphabetSize = 16; // A-P (16 characters)
        private const int DefaultPrecision = 24; // Increased precision for better compression
        private const int UpperLimit = (1 << DefaultPrecision) - 1;
        private const int MinFrequency = 1;
        private const int FrequencyTableSize = AlphabetSize * sizeof(ushort); // Use ushort for frequencies

        public struct EncodedResult
        {
            public byte[] Data;
            public ushort[] Frequencies; // Smaller frequency storage
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static EncodedResult Encode(string input)
        {
            if (string.IsNullOrEmpty(input))
                return new EncodedResult { Data = Array.Empty<byte>(), Frequencies = Array.Empty<ushort>() };

            // Calculate and validate frequencies
            Span<int> rawFrequencies = stackalloc int[AlphabetSize];
            foreach (var c in input)
            {
                if (c < 'A' || c > 'P')
                    throw new ArgumentException("String must contain only characters from A to P");
                rawFrequencies[c - 'A']++;
            }

            // Normalize frequencies to fit in ushort while maintaining proportions
            ushort[] frequencies = NormalizeFrequencies(rawFrequencies);

            // Calculate scaled cumulative frequencies
            Span<int> cumulativeFreq = stackalloc int[AlphabetSize + 2];
            int total = Sum(frequencies);
            for (int i = 0; i < AlphabetSize; i++)
            {
                cumulativeFreq[i + 1] = cumulativeFreq[i] + (int)((long)frequencies[i] * UpperLimit / total);
            }
            cumulativeFreq[AlphabetSize + 1] = UpperLimit + 1;

            // Initialize encoding state with adaptive buffer
            uint low = 0;
            uint high = 0xFFFFFFFF;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(input.Length / 2, 64)); // Start with smaller buffer
            int position = 0;

            try
            {
                // Encode each character with optimized range calculation
                foreach (var c in input)
                {
                    int index = c - 'A';
                    EncodeSymbol(ref low, ref high, cumulativeFreq[index], cumulativeFreq[index + 1],
                                DefaultPrecision, ref buffer, ref position);
                }

                // Finalize encoding
                FlushEncoder(ref low, ref buffer, ref position);

                return new EncodedResult
                {
                    Data = buffer.AsSpan(0, position).ToArray(),
                    Frequencies = frequencies
                };
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static ushort[] NormalizeFrequencies(Span<int> rawFrequencies)
        {
            // Find max frequency to scale down to ushort range
            int maxFreq = Max(rawFrequencies);
            ushort[] frequencies = new ushort[AlphabetSize];

            if (maxFreq > ushort.MaxValue)
            {
                // Scale down proportionally
                double scale = (double)ushort.MaxValue / maxFreq;
                for (int i = 0; i < AlphabetSize; i++)
                {
                    frequencies[i] = (ushort)Math.Max(MinFrequency, (int)(rawFrequencies[i] * scale));
                }
            }
            else
            {
                // Use as-is with minimum frequency
                for (int i = 0; i < AlphabetSize; i++)
                {
                    frequencies[i] = (ushort)Math.Max(MinFrequency, rawFrequencies[i]);
                }
            }
            return frequencies;
        }

        private static int Max(Span<int> span)
        {
            int max = int.MinValue;
            foreach (var item in span)
            {
                if (item > max) max = item;
            }
            return max;
        }

        private static int Sum(ushort[] array)
        {
            int sum = 0;
            foreach (var item in array)
            {
                sum += item;
            }
            return sum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decode(byte[] encodedData, ushort[] frequencies, int originalLength)
        {
            if (encodedData == null || frequencies == null || frequencies.Length != AlphabetSize || originalLength <= 0)
                return string.Empty;

            // Reconstruct cumulative frequencies
            Span<int> cumulativeFreq = stackalloc int[AlphabetSize + 2];
            int total = Sum(frequencies);
            for (int i = 0; i < AlphabetSize; i++)
            {
                cumulativeFreq[i + 1] = cumulativeFreq[i] + (int)((long)frequencies[i] * UpperLimit / total);
            }
            cumulativeFreq[AlphabetSize + 1] = UpperLimit + 1;

            // Initialize decoder
            uint low = 0;
            uint high = 0xFFFFFFFF;
            uint value = 0;
            int position = 0;

            // Read initial 4 bytes
            for (int i = 0; i < 4 && position < encodedData.Length; i++)
            {
                value = (value << 8) | encodedData[position++];
            }

            var result = new StringBuilder(originalLength);
            while (result.Length < originalLength)
            {
                int index = DecodeSymbol(ref low, ref high, ref value, cumulativeFreq,
                                        AlphabetSize + 1, DefaultPrecision, encodedData, ref position);

                if (index >= AlphabetSize) break;

                result.Append((char)('A' + index));
            }

            return result.ToString();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EncodeSymbol(ref uint low, ref uint high, int cmin, int cmax,
                                       int precision, ref byte[] buffer, ref int position)
        {
            ulong range = (ulong)(high - low) + 1;
            high = low + (uint)((range * (ulong)cmax) >> precision) - 1;
            low += (uint)((range * (ulong)cmin) >> precision);

            while ((low ^ high) < (1 << 24))
            {
                WriteByte((byte)(low >> 24), ref buffer, ref position);
                low <<= 8;
                high = (high << 8) | 0xFF;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DecodeSymbol(ref uint low, ref uint high, ref uint value,
                                      Span<int> cumulativeFreq, int size, int precision,
                                      byte[] encodedData, ref int position)
        {
            ulong range = (ulong)(high - low) + 1;
            ulong offset = (ulong)(value - low);
            ulong scaledValue = ((offset + 1) << precision) - 1;
            uint total = (uint)(cumulativeFreq[size] - cumulativeFreq[0]);
            uint point = (uint)(scaledValue / range);

            // Optimized binary search
            int index = 0;
            while (index < size && point >= cumulativeFreq[index + 1])
            {
                index++;
            }

            // Update range
            range = (ulong)(high - low) + 1;
            high = low + (uint)((range * (ulong)cumulativeFreq[index + 1]) >> precision) - 1;
            low += (uint)((range * (ulong)cumulativeFreq[index]) >> precision);

            // Read more bytes if needed
            while ((low ^ high) < (1 << 24))
            {
                low <<= 8;
                high = (high << 8) | 0xFF;
                value = (value << 8) | (position < encodedData.Length ? encodedData[position++] : (byte)0);
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void FlushEncoder(ref uint low, ref byte[] buffer, ref int position)
        {
            low++;
            for (int i = 0; i < 4; i++)
            {
                WriteByte((byte)(low >> 24), ref buffer, ref position);
                low <<= 8;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteByte(byte b, ref byte[] buffer, ref int position)
        {
            if (position >= buffer.Length)
            {
                byte[] newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = newBuffer;
            }
            buffer[position++] = b;
        }
    }
}
