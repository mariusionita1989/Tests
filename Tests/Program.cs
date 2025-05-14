using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Running;
using Tests;
using Tests.Benchmarks;

public class Program
{
    public static void Main()
    {
        //BenchmarkRunner.Run<TestsDemo>();

        // Generate a random string of A,B,C,D characters
        const int length = 32 * 1024;
        string original = CustomizedRandomStringGenerator.Generate(length);
        Console.WriteLine($"Original string ({length} chars):");
        Console.WriteLine(original.Substring(0, Math.Min(100, length)) + (length > 100 ? "..." : ""));
        Console.WriteLine();

        // Compression pipeline: BWT → MTF → RLE
        Console.WriteLine("Compressing...");
        var stopwatch = Stopwatch.StartNew();

        // 1. Apply BWT
        string bwtTransformed = CustomBWT.Compress(original);
        Console.WriteLine($"BWT complete. Transformed length: {bwtTransformed.Length} chars");

        // 2. Apply MTF
        byte[] mtfEncoded = MTF.Encode(bwtTransformed.AsSpan());
        Console.WriteLine($"MTF complete. Encoded length: {mtfEncoded.Length} bytes");

        // 3. Apply RLE
        byte[] rleCompressed = RLE.Compress(Encoding.ASCII.GetString(mtfEncoded).AsSpan());
        Console.WriteLine($"RLE complete. Final compressed length: {rleCompressed.Length} bytes");

        stopwatch.Stop();
        Console.WriteLine($"Compression completed in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine();

        // Decompression pipeline: RLE → MTF → BWT
        Console.WriteLine("Decompressing...");
        stopwatch.Restart();

        // 1. Reverse RLE
        string rleDecompressed = RLE.Decompress(rleCompressed);
        Console.WriteLine($"RLE reversed. Length: {rleDecompressed.Length} chars");

        // 2. Reverse MTF
        string mtfDecoded = MTF.Decode(Encoding.ASCII.GetBytes(rleDecompressed));
        Console.WriteLine($"MTF reversed. Length: {mtfDecoded.Length} chars");

        // 3. Reverse BWT
        string finalOutput = CustomBWT.Decompress(mtfDecoded);
        Console.WriteLine($"BWT reversed. Length: {finalOutput.Length} chars");

        stopwatch.Stop();
        Console.WriteLine($"Decompression completed in {stopwatch.ElapsedMilliseconds} ms");
        Console.WriteLine();

        // Verify the result matches the original
        bool success = original.Equals(finalOutput, StringComparison.Ordinal);
        Console.WriteLine($"Verification: {(success ? "SUCCESS" : "FAILED")}");
        Console.WriteLine();

        // Display compression ratio
        double originalSize = length * sizeof(char); // Each char is 2 bytes in .NET
        double compressedSize = rleCompressed.Length;
        double ratio = originalSize / compressedSize;
        Console.WriteLine($"Original size: {originalSize} bytes");
        Console.WriteLine($"Compressed size: {compressedSize} bytes");
        Console.WriteLine($"Compression ratio: {ratio:0.##}:1");
    }
}