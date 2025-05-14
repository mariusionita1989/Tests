using System.Runtime.CompilerServices;

namespace Tests
{
    public static class ByteArrayCompression
    {
        private const int length = 128 * 1024;
        private static byte[] input = Array.Empty<byte>();

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static void CompressByte()
        {
            input = RandomByteGenerator.Generate(length);
            int arrayLength = input.Length;
            Console.WriteLine($"Original byte array length {arrayLength}");
            if (arrayLength > 0)
            {
                byte[] bwtOutput = ByteBWT.Compress(input);
                byte[] mtfResult = ByteMTF.Encode(bwtOutput);
                byte[] rleOutput = ByteRLE.Compress(mtfResult);
            }
        }
    }
}
