using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tests
{
    public static class UInt16LzwProcessor
    {
        private const int InitialDictSize = 65536;        // All possible ushort values (2^16)
        private const int MaxDictSize = 1 << 20;          // 20-bit codes (0-1,048,575)
        private const int InitialBitLength = 17;          // Start with 17-bit codes
        private const int MaxBitLength = 20;              // Grow to 20-bit codes
        private const int DictionaryResetThreshold = 1 << 20; // Reset before hitting max

        private readonly struct UInt16SpanKey : IEquatable<UInt16SpanKey>
        {
            private readonly ushort[] _values;
            private readonly int _hashCode;

            public UInt16SpanKey(ReadOnlySpan<ushort> span)
            {
                _values = ArrayPool<ushort>.Shared.Rent(span.Length);
                span.CopyTo(_values);
                _hashCode = ComputeHashCode(span);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            private static int ComputeHashCode(ReadOnlySpan<ushort> span)
            {
                unchecked
                {
                    int hash = 5381;
                    foreach (ushort value in span)
                        hash = ((hash << 5) + hash) ^ value;
                    return hash;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public bool Equals(UInt16SpanKey other)
            {
                if (_values.Length != other._values.Length)
                    return false;

                return MemoryMarshal.CreateReadOnlySpan(ref _values[0], _values.Length)
                    .SequenceEqual(MemoryMarshal.CreateReadOnlySpan(ref other._values[0], other._values.Length));
            }

            public override bool Equals(object? obj) => obj is UInt16SpanKey other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public override int GetHashCode() => _hashCode;

            public void ReturnBuffer()
            {
                if (_values != null)
                    ArrayPool<ushort>.Shared.Return(_values);
            }
        }

        private class UInt16SpanKeyComparer : IEqualityComparer<UInt16SpanKey>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public bool Equals(UInt16SpanKey x, UInt16SpanKey y) => x.Equals(y);

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public int GetHashCode(UInt16SpanKey obj) => obj.GetHashCode();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static ushort[] Compress(ReadOnlySpan<ushort> input)
        {
            if (input.IsEmpty)
                return Array.Empty<ushort>();

            var dictionary = new Dictionary<UInt16SpanKey, int>(
                capacity: MaxDictSize,
                comparer: new UInt16SpanKeyComparer());

            // Initialize dictionary with all possible ushort values
            for (int i = 0; i < InitialDictSize; i++)
            {
                ushort[] singleValue = { (ushort)i };
                dictionary[new UInt16SpanKey(singleValue)] = i;
            }

            var codes = new List<int>(input.Length);
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            try
            {
                int pos = 0;
                while (pos < input.Length)
                {
                    // Find the longest sequence starting at pos that exists in the dictionary
                    int bestLength = 1;
                    int bestCode = input[pos]; // Single character codes always exist

                    // Search for longer sequences (up to remaining length)
                    for (int len = 2; len <= input.Length - pos; len++)
                    {
                        var candidate = new UInt16SpanKey(input.Slice(pos, len));
                        if (dictionary.TryGetValue(candidate, out int code))
                        {
                            bestLength = len;
                            bestCode = code;
                            candidate.ReturnBuffer();
                        }
                        else
                        {
                            candidate.ReturnBuffer();
                            break; // No longer sequence found
                        }
                    }

                    // Output the code for the longest found sequence
                    codes.Add(bestCode);

                    // Add new sequence to dictionary (current sequence + next character)
                    if (pos + bestLength < input.Length && dictionary.Count < DictionaryResetThreshold)
                    {
                        ushort[] newSequence = new ushort[bestLength + 1];
                        input.Slice(pos, bestLength).CopyTo(newSequence);
                        newSequence[bestLength] = input[pos + bestLength];
                        dictionary[new UInt16SpanKey(newSequence)] = dictionary.Count;

                        // Check if we need to increase bit length
                        if (dictionary.Count > maxCode && currentBitLength < MaxBitLength)
                        {
                            currentBitLength++;
                            maxCode = (1 << currentBitLength) - 1;
                        }
                    }

                    pos += bestLength;

                    // Reset dictionary if we're approaching max capacity
                    if (dictionary.Count >= DictionaryResetThreshold)
                    {
                        foreach (var key in dictionary.Keys)
                            key.ReturnBuffer();
                        dictionary.Clear();
                        for (int i = 0; i < InitialDictSize; i++)
                        {
                            ushort[] singleValue = { (ushort)i };
                            dictionary[new UInt16SpanKey(singleValue)] = i;
                        }
                        currentBitLength = InitialBitLength;
                        maxCode = (1 << currentBitLength) - 1;
                    }
                }
            }
            finally
            {
                // Clean up any remaining buffers
                foreach (var key in dictionary.Keys)
                    key.ReturnBuffer();
            }

            return PackCodes(codes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static ushort[] PackCodes(List<int> codes)
        {
            int codeCount = codes.Count;
            if (codeCount == 0)
                return Array.Empty<ushort>();

            // Calculate total bits needed
            int totalBits = 0;
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            for (int i = 0; i < codeCount; i++)
            {
                totalBits += currentBitLength;
                if (codes[i] == maxCode && currentBitLength < MaxBitLength)
                {
                    currentBitLength++;
                    maxCode = (1 << currentBitLength) - 1;
                }
            }

            // Calculate output size in ushorts (rounding up)
            int outputSize = (totalBits + 15) >> 4;
            ushort[] output = GC.AllocateUninitializedArray<ushort>(outputSize);
            int buffer = 0;
            int bitsInBuffer = 0;
            int outputPos = 0;
            currentBitLength = InitialBitLength;
            maxCode = (1 << currentBitLength) - 1;

            for (int i = 0; i < codeCount; i++)
            {
                int code = codes[i];
                int remainingBits = currentBitLength;

                while (remainingBits > 0)
                {
                    int freeBits = 16 - bitsInBuffer;
                    int bitsToWrite = remainingBits < freeBits ? remainingBits : freeBits;
                    int mask = (1 << bitsToWrite) - 1;
                    buffer |= (code & mask) << bitsInBuffer;
                    bitsInBuffer += bitsToWrite;
                    remainingBits -= bitsToWrite;
                    code >>= bitsToWrite;

                    if (bitsInBuffer == 16)
                    {
                        output[outputPos++] = (ushort)buffer;
                        buffer = 0;
                        bitsInBuffer = 0;
                    }
                }

                if (codes[i] == maxCode && currentBitLength < MaxBitLength)
                {
                    currentBitLength++;
                    maxCode = (1 << currentBitLength) - 1;
                }
            }

            // Flush remaining bits
            if (bitsInBuffer > 0)
                output[outputPos] = (ushort)buffer;

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static ushort[] Decompress(ReadOnlySpan<ushort> compressed)
        {
            int compressedLength = compressed.Length;
            if (compressedLength == 0)
                return Array.Empty<ushort>();

            var codes = UnpackCodes(compressed);
            int codeCount = codes.Count;
            if (codeCount == 0)
                return Array.Empty<ushort>();

            var dictionary = new Dictionary<int, ushort[]>(MaxDictSize);
            for (int i = 0; i < InitialDictSize; i++)
                dictionary[i] = new ushort[] { (ushort)i };

            ushort[] previous = dictionary[codes[0]];
            var output = new List<ushort>(compressedLength << 1); // Estimate output size
            output.AddRange(previous);

            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            for (int i = 1; i < codeCount; i++)
            {
                int code = codes[i];
                ushort[] entry;

                if (dictionary.TryGetValue(code, out entry))
                {
                    // Existing code
                }
                else if (code == dictionary.Count)
                {
                    // Special case for pattern growth
                    entry = GC.AllocateUninitializedArray<ushort>(previous.Length + 1);
                    previous.AsSpan().CopyTo(entry);
                    entry[^1] = previous[0];
                }
                else
                {
                    throw new InvalidOperationException("Invalid LZW code");
                }

                output.AddRange(entry);

                // Add to dictionary if space available
                if (dictionary.Count < DictionaryResetThreshold)
                {
                    ushort[] newEntry = GC.AllocateUninitializedArray<ushort>(previous.Length + 1);
                    previous.AsSpan().CopyTo(newEntry);
                    newEntry[^1] = entry[0];
                    dictionary[dictionary.Count] = newEntry;

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
                        dictionary[j] = new ushort[] { (ushort)j };
                    currentBitLength = InitialBitLength;
                    maxCode = (1 << currentBitLength) - 1;
                }

                previous = entry;
            }

            return output.ToArray();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private static List<int> UnpackCodes(ReadOnlySpan<ushort> compressed)
        {
            int compressedLength = compressed.Length;
            if (compressedLength == 0)
                return new List<int>();

            // Estimate initial capacity (compressedLength * 16 / InitialBitLength)
            int estimatedCapacity = (compressedLength << 4) / InitialBitLength;
            var codes = new List<int>(estimatedCapacity);
            int buffer = 0;
            int bitsInBuffer = 0;
            int currentBitLength = InitialBitLength;
            int maxCode = (1 << currentBitLength) - 1;

            for (int i = 0; i < compressedLength; i++)
            {
                buffer |= compressed[i] << bitsInBuffer;
                bitsInBuffer += 16;

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
