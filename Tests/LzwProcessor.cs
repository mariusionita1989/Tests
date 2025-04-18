using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tests
{
    public static class LzwProcessor
    {
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(SpanKey other)
            {
                if (_length != other._length) return false;
                return _source.AsSpan(_start, _length)
                              .SequenceEqual(other._source.AsSpan(other._start, other._length));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override bool Equals(object? obj) =>
                obj is SpanKey other && Equals(other);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode()
            {
                var span = _source.AsSpan(_start, _length);
                var bytes = MemoryMarshal.AsBytes(span);
                unchecked
                {
                    int hash = 5381;
                    for (int i = 0; i < bytes.Length; i++)
                        hash = ((hash << 5) + hash) ^ bytes[i];
                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static List<int> Compress(string uncompressed)
        {
            const int InitialDictSize = 256;
            Dictionary<SpanKey, int> dictionary = new(8192);

            for (int i = 0; i < InitialDictSize; i++)
                dictionary[new SpanKey(((char)i).ToString(), 0, 1)] = i;

            List<int> output = new(uncompressed.Length >> 1);
            int start = 0, length = 1;
            int inputLength = uncompressed.Length;

            while (start + length <= inputLength)
            {
                var candidate = new SpanKey(uncompressed, start, length);

                if (dictionary.TryGetValue(candidate, out _))
                {
                    length++;
                    continue;
                }

                var previous = new SpanKey(uncompressed, start, length - 1);
                output.Add(dictionary[previous]);
                dictionary[candidate] = dictionary.Count;

                start += length - 1;
                length = 1;
            }

            if (length > 1)
            {
                var last = new SpanKey(uncompressed, start, length - 1);
                output.Add(dictionary[last]);
            }

            return output;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decompress(List<int> compressed)
        {
            if (compressed.Count == 0)
                return string.Empty;

            const int InitialDictSize = 256;
            Dictionary<int, string> dictionary = new(8192);

            for (int i = 0; i < InitialDictSize; i++)
                dictionary[i] = ((char)i).ToString();

            var span = CollectionsMarshal.AsSpan(compressed);
            string previous = dictionary[span[0]];
            StringBuilder output = new(compressed.Count * 2);
            output.Append(previous);

            for (int i = 1; i < span.Length; i++)
            {
                int code = span[i];
                string entry;

                if (dictionary.TryGetValue(code, out var val))
                {
                    entry = val;
                }
                else if (code == dictionary.Count)
                {
                    entry = previous + previous[0];
                }
                else
                {
                    throw new InvalidOperationException("Invalid LZW stream.");
                }

                output.Append(entry);
                dictionary[dictionary.Count] = previous + entry[0];
                previous = entry;
            }

            return output.ToString();
        }
    }
}
