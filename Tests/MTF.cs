using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Tests
{
    public static class MTF
    {
        // The size of the alphabet used in Move-To-Front encoding: 'A', 'B', 'C', 'D'
        private const int AlphabetSize = 4;

        // Initial alphabet order before any encoding or decoding
        private static readonly char[] InitialAlphabet = ['A', 'B', 'C', 'D'];

        // Maximum input length supported: 256KB
        private const int MaxInputLength = 256 * 1024;

        // Move-To-Front Encoding Method
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe byte[] Encode(ReadOnlySpan<char> input)
        {
            if (input.Length > MaxInputLength)
                throw new ArgumentException($"Input too large (max {MaxInputLength} chars)");

            // Allocate a small char buffer on the stack for the alphabet
            Span<char> alphabet = stackalloc char[AlphabetSize];
            InitialAlphabet.AsSpan().CopyTo(alphabet); // Copy initial order

            // Each symbol is encoded in 2 bits (since 4 symbols => 2 bits per symbol)
            int bitLength = input.Length << 1;
            int encodedBytes = (bitLength + 7) >> 3; // Round up bits to bytes
            int totalOutputBytes = 3 + encodedBytes; // Add 3 bytes for header
            byte[] output = new byte[totalOutputBytes];

            // Write 24-bit big-endian length prefix to first 3 bytes
            output[0] = (byte)(input.Length >> 16);
            output[1] = (byte)(input.Length >> 8);
            output[2] = (byte)input.Length;

            fixed (byte* outputPtr = output)
            fixed (char* inputPtr = input)
            {
                byte* outPtr = outputPtr + 3; // Skip header
                ulong accumulator = 0;       // Bit accumulator (writes in 64-bit chunks)
                int bitsFilled = 0;          // Number of bits in the accumulator

                for (int i = 0; i < input.Length; i++)
                {
                    char c = inputPtr[i];
                    int index = 0;

                    // Linear search for character in alphabet (O(1) since it's only 4 items)
                    if (alphabet[1] == c) index = 1;
                    else if (alphabet[2] == c) index = 2;
                    else if (alphabet[3] == c) index = 3;

                    // Append 2-bit index to the accumulator
                    accumulator |= ((ulong)index << bitsFilled);
                    bitsFilled += 2;

                    // Move-to-front: bring the used character to front of the alphabet
                    if (index != 0)
                    {
                        var temp = alphabet[index];
                        for (int j = index; j > 0; j--)
                            alphabet[j] = alphabet[j - 1];
                        alphabet[0] = temp;
                    }

                    // Write to output when accumulator is full (64 bits)
                    if (bitsFilled == 64)
                    {
                        *(ulong*)outPtr = accumulator;
                        outPtr += 8;
                        accumulator = 0;
                        bitsFilled = 0;
                    }
                }

                // Write any remaining bits in the accumulator (if not aligned to 64 bits)
                if (bitsFilled > 0)
                {
                    int remainingBytes = (bitsFilled + 7) >> 3;
                    for (int i = 0; i < remainingBytes; i++)
                    {
                        *outPtr++ = (byte)(accumulator & 0xFF);
                        accumulator >>= 8;
                    }
                }
            }

            return output;
        }

        // Move-To-Front Decoding Method
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe string Decode(ReadOnlySpan<byte> input)
        {
            if (input.Length < 3)
                throw new ArgumentException("Invalid input (missing length prefix)");

            // Read 24-bit big-endian length header (original number of characters)
            int originalLength = (input[0] << 16) | (input[1] << 8) | input[2];
            if (originalLength > MaxInputLength)
                throw new ArgumentException($"Decoded length too large (max {MaxInputLength} chars)");

            // Stackalloc a local alphabet copy
            Span<char> alphabet = stackalloc char[AlphabetSize];
            InitialAlphabet.AsSpan().CopyTo(alphabet);

            // Allocate output buffer using stackalloc for small sizes, or ArrayPool for larger ones
            char[]? arrayPool = null;
            Span<char> output = originalLength <= 1024 ?
                stackalloc char[originalLength] :
                (arrayPool = ArrayPool<char>.Shared.Rent(originalLength)).AsSpan(0, originalLength);

            try
            {
                fixed (byte* inputPtr = input)
                fixed (char* outputPtr = output)
                {
                    byte* currentInput = inputPtr + 3; // Skip header
                    char* currentOutput = outputPtr;

                    int totalBits = originalLength << 1; // 2 bits per symbol
                    int bitsRead = 0;
                    ulong buffer = 0;
                    int availableBits = 0;

                    while (bitsRead < totalBits)
                    {
                        // Load more bits into buffer if needed
                        if (availableBits < 2)
                        {
                            buffer |= ((ulong)(*currentInput++)) << availableBits;
                            availableBits += 8;
                        }

                        // Extract 2-bit symbol index
                        int index = (int)(buffer & 0b11);
                        buffer >>= 2;
                        availableBits -= 2;
                        bitsRead += 2;

                        // Write corresponding character to output
                        *currentOutput++ = alphabet[index];

                        // Move-to-front update
                        if (index != 0)
                        {
                            var temp = alphabet[index];
                            for (int j = index; j > 0; j--)
                                alphabet[j] = alphabet[j - 1];
                            alphabet[0] = temp;
                        }
                    }
                }

                return new string(output);
            }
            finally
            {
                // Return buffer to ArrayPool if it was used
                if (arrayPool != null)
                    ArrayPool<char>.Shared.Return(arrayPool);
            }
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

