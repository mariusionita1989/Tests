using System.Runtime.CompilerServices;

namespace Tests
{
    public static class StringCompression
    {
        private const int length = 128 * 1024;
        private static string hex = string.Empty;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static void CompressStr() 
        {
            hex = CustomizedRandomStringGenerator.Generate(length);
            Console.WriteLine($"Original string length {hex.Length<<1}");
            if (!string.IsNullOrEmpty(hex))
            {
                string bwtOutput = CustomBWT.Compress(hex);
                byte[] mtfResult = MTF.Encode(bwtOutput);
                string mtfOutput = MTF.ToSymbolString(mtfResult);
                byte[] rleOutput = RLE.Compress(mtfOutput);
                string rleResult = RLE.ToSymbolString(rleOutput);
                string reverseRle = RLE.Decompress(rleOutput);
            }
        }
    }
}
