using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;

namespace Tests
{
    public static class CustomizedRandomStringGenerator
    {
        private const int CharsPerByte = 4; // 2 bits per char, 4 chars per byte
        private const int VectorSize = 32; // AVX2 vector size in bytes
        private const int CharsPerVector = VectorSize * CharsPerByte;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Generate(int length)
        {
            if (length <= 0) return string.Empty;

            int requiredBytes = (length + CharsPerByte - 1) >> 2;
            byte[] randomBytes = RandomNumberGenerator.GetBytes(requiredBytes);
            return ConvertToString(randomBytes, length);
        }

        private static unsafe string ConvertToString(byte[] input, int outputLength)
        {
            return string.Create(outputLength, input, (span, inputBytes) =>
            {
                fixed (byte* inputPtr = inputBytes)
                fixed (char* outputPtr = span)
                {
                    int inputOffset = 0;
                    int outputOffset = 0;
                    int remainingChars = outputLength;

                    if (Avx2.IsSupported)
                    {
                        Vector256<byte> lut = Vector256.Create(
                            (byte)'A', (byte)'B', (byte)'C', (byte)'D', 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0,
                            0, 0, 0, 0, 0, 0, 0, 0
                        );

                        Vector256<byte> mask = Vector256.Create((byte)0x03);

                        while (remainingChars >= CharsPerVector && inputOffset + VectorSize <= inputBytes.Length)
                        {
                            Vector256<byte> bytes = Avx.LoadVector256(inputPtr + inputOffset);

                            // Process 32 bytes -> 128 characters
                            for (int i = 0; i < VectorSize && outputOffset + 4 <= outputLength; i++)
                            {
                                byte b = bytes.GetElement(i);
                                outputPtr[outputOffset++] = (char)('A' + (b & 0x03));
                                outputPtr[outputOffset++] = (char)('A' + ((b >> 2) & 0x03));
                                outputPtr[outputOffset++] = (char)('A' + ((b >> 4) & 0x03));
                                outputPtr[outputOffset++] = (char)('A' + ((b >> 6) & 0x03));
                            }

                            inputOffset += VectorSize;
                            remainingChars -= CharsPerVector;
                        }
                    }

                    // Process remaining data in chunks of 4 bytes (16 chars)
                    while (remainingChars >= 16 && inputOffset + 4 <= inputBytes.Length)
                    {
                        uint fourBytes = *(uint*)(inputPtr + inputOffset);

                        for (int i = 0; i < 4 && outputOffset + 4 <= outputLength; i++)
                        {
                            byte currentByte = (byte)(fourBytes >> (i * 8));
                            outputPtr[outputOffset++] = (char)('A' + (currentByte & 0x03));
                            outputPtr[outputOffset++] = (char)('A' + ((currentByte >> 2) & 0x03));
                            outputPtr[outputOffset++] = (char)('A' + ((currentByte >> 4) & 0x03));
                            outputPtr[outputOffset++] = (char)('A' + ((currentByte >> 6) & 0x03));
                        }

                        inputOffset += 4;
                        remainingChars -= 16;
                    }

                    // Process remaining bytes
                    for (; inputOffset < inputBytes.Length && outputOffset < outputLength; inputOffset++)
                    {
                        byte b = inputPtr[inputOffset];
                        if (outputOffset < outputLength) outputPtr[outputOffset++] = (char)('A' + (b & 0x03));
                        if (outputOffset < outputLength) outputPtr[outputOffset++] = (char)('A' + ((b >> 2) & 0x03));
                        if (outputOffset < outputLength) outputPtr[outputOffset++] = (char)('A' + ((b >> 4) & 0x03));
                        if (outputOffset < outputLength) outputPtr[outputOffset++] = (char)('A' + ((b >> 6) & 0x03));
                    }
                }
            });
        }
    }
}
