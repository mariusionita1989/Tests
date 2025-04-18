using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Tests.Benchmarks
{
    [MemoryDiagnoser]
    [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    [RankColumn]
    public class TestsDemo
    {
        private const int length = 256 * 1024;
        private string hex = string.Empty;
        private string nhex = string.Empty;
        private char[] cHex = Array.Empty<char>();
        private char[] cnHex = Array.Empty<char>();
        private byte[] bHex = Array.Empty<byte>();

        [GlobalSetup]
        public void GlobalSetup()
        {
            hex = CustomizedRandomHexGenerator.Generate(length);
            nhex = CustomizedRandomHexGenerator.Generate(length);
            cHex = hex.AsSpan().ToArray();
            cnHex = nhex.AsSpan().ToArray();
            bHex = RandomByteGenerator.Generate(length);
        }

        [Benchmark(Baseline = true)]
        public void Tests_RandomByteGenerator()
        {
            var result = RandomByteGenerator.Generate(length);
        }

        //[Benchmark]
        //public void Tests_RangeMapper()
        //{
        //    var result = RangeMapper.Encode(hex);
        //}

        [Benchmark]
        public void Tests_LzwStringCompression()
        {
            var result = LzwProcessor.Compress(hex);
        }

        //[Benchmark]
        //public void Tests_RandomTextGenerator()
        //{
        //    var result = RandomHexGenerator.Generate(length);
        //}

        //[Benchmark]
        //public void Tests_CustomizedRandomHexGenerator()
        //{
        //    var result = CustomizedRandomHexGenerator.Generate(length);
        //}

        //[Benchmark]
        //public void Tests_MTFCompress()
        //{
        //    var result = MTF.Compress(hex);
        //}

        //[Benchmark]
        //public void Tests_BWTCompress()
        //{
        //    var result = BWT.Compress(hex);
        //}

        //[Benchmark]
        //public void Tests_FastStringComparer()
        //{
        //    var result = FastComparer.Compare(hex, nhex);
        //}

        //[Benchmark]
        //public void Tests_UnsafeMemcmpCallCompare()
        //{
        //    var result = FastComparer.UnsafeMemcmpCallCompare(cHex, cnHex);
        //}
    }
}
