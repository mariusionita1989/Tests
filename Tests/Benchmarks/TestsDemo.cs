using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Tests.Benchmarks
{
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [RankColumn]
    public class TestsDemo
    {
        private const int length = 512 * 1024;
        //private string hex = string.Empty;
        //private char[] a = Array.Empty<char>();
        //private char[] b = Array.Empty<char>();
        //private string c = string.Empty;
        //private string d = string.Empty;
        private byte[] e = Array.Empty<byte>();
        //private byte[] f = Array.Empty<byte>();

        [GlobalSetup]
        public void GlobalSetup()
        {
            //hex = CustomizedRandomStringGenerator.Generate(length);
            //a = CustomizedRandomStringGenerator.Generate(length).ToCharArray();
            //b = CustomizedRandomStringGenerator.Generate(length).ToCharArray();
            //c = CustomizedRandomStringGenerator.Generate(length);
            //d = CustomizedRandomStringGenerator.Generate(length);
            e = RandomByteGenerator.Generate(length);
            //f = RandomByteGenerator.Generate(length);
        }

        //[Benchmark(Baseline = true)]
        //public void Tests_CustomRandomStringGenerator()
        //{
        //    var result = CustomizedRandomStringGenerator.Generate(length);
        //}

        //[Benchmark]
        //public void Tests_LzwStringCompression()
        //{
        //    var result = LzwProcessor.Compress(hex);
        //}

        //[Benchmark(Baseline = true)]
        //public void Tests_MTFCompress()
        //{
        //    var result = MTF.Encode(hex);
        //}

        //[Benchmark]
        //public void Tests_RLECompress()
        //{
        //    var result = RLE.Compress(hex);
        //}

        //[Benchmark(Baseline = true)]
        //public void Tests_CustomBWTCompress()
        //{
        //    var result = CustomBWT.Compress(customHex);
        //}

        //[Benchmark(Baseline = true)]
        //public void Tests_StringCompress()
        //{
        //    StringCompression.CompressStr();
        //}

        //[Benchmark(Baseline = true)]
        //public void Tests_ByteArrayCompress()
        //{
        //    var result = ByteBWT.Compress(e);
        //}

        //[Benchmark]
        //public void Tests_ByteArrayMTFCompress()
        //{
        //    var result = ByteMTF.Encode(e);
        //}

        //[Benchmark]
        //public void Tests_ByteArrayRLECompress()
        //{
        //    var result = ByteRLE.Compress(e);
        //}

        [Benchmark(Baseline = true)]
        public void Tests_ImprovedSimdUltraFastHashCode()
        {
            var result = HashFunction.ImprovedSimdUltraFastHashCode(e, 0, length - 1);
        }

        [Benchmark]
        public void Tests_UltraFastFNV64ComputeHashAvx2()
        {
            var result = HashFunction.UltraFastFNV64ComputeHashAvx2(e);
        }
    }
}
