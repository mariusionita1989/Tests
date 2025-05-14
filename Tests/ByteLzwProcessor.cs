using System.Buffers;
using System.Runtime.CompilerServices;

namespace Tests
{
    public static class ByteLzwProcessor
    {
        private const int InitialDictionarySize = 256;
        private const int MaxDictionarySize = 65536;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static List<int> Compress(ReadOnlySpan<byte> uncompressed)
        {
            var dictionary = new Dictionary<ByteSequence, int>(InitialDictionarySize, ByteSequenceEqualityComparer.Instance);

            for (int i = 0; i < InitialDictionarySize; i++)
            {
                var temp = ArrayPool<byte>.Shared.Rent(1);
                temp[0] = (byte)i;
                dictionary.Add(new ByteSequence(temp, 1), i);
            }

            var result = new List<int>(uncompressed.Length / 2);
            using var currentSequence = new ByteSequenceBuilder();

            foreach (byte b in uncompressed)
            {
                currentSequence.Append(b);

                var currentAsSequence = currentSequence.ToByteSequence();
                if (!dictionary.TryGetValue(currentAsSequence, out int code))
                {
                    if (currentSequence.Length > 1)
                    {
                        var previousSequence = currentSequence.GetSubSequence(0, currentSequence.Length - 1);
                        result.Add(dictionary[previousSequence]);
                        previousSequence.Dispose();
                    }
                    else
                    {
                        result.Add(currentSequence[0]);
                    }

                    if (dictionary.Count < MaxDictionarySize)
                    {
                        var newEntry = currentSequence.ToByteSequence();
                        dictionary.Add(newEntry, dictionary.Count);
                    }

                    currentSequence.Clear();
                    currentSequence.Append(b);
                }
                currentAsSequence.Dispose();
            }

            if (currentSequence.Length > 0)
            {
                var finalSequence = currentSequence.ToByteSequence();
                result.Add(dictionary[finalSequence]);
                finalSequence.Dispose();
            }

            foreach (var key in dictionary.Keys)
            {
                key.Dispose();
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static byte[] Decompress(List<int> compressed)
        {
            if (compressed == null || compressed.Count == 0)
                return Array.Empty<byte>();

            var dictionary = new Dictionary<int, ByteSequence>(InitialDictionarySize);

            for (int i = 0; i < InitialDictionarySize; i++)
            {
                var temp = ArrayPool<byte>.Shared.Rent(1);
                temp[0] = (byte)i;
                dictionary.Add(i, new ByteSequence(temp, 1));
            }

            using var output = new ByteSequenceBuilder(compressed.Count * 2);
            using var previousEntry = new ByteSequenceBuilder();

            if (!dictionary.TryGetValue(compressed[0], out var firstEntry))
                throw new ArgumentException("Invalid compressed data");

            output.Append(firstEntry);
            previousEntry.Append(firstEntry);

            for (int i = 1; i < compressed.Count; i++)
            {
                int code = compressed[i];
                ByteSequence currentEntry;

                if (dictionary.TryGetValue(code, out currentEntry))
                {
                    output.Append(currentEntry);

                    if (dictionary.Count < MaxDictionarySize)
                    {
                        using var newEntry = new ByteSequenceBuilder(previousEntry.Length + 1);
                        newEntry.Append(previousEntry.ToByteSequence());
                        newEntry.Append(currentEntry[0]);
                        dictionary.Add(dictionary.Count, newEntry.ToByteSequence());
                    }

                    previousEntry.Clear();
                    previousEntry.Append(currentEntry);
                }
                else if (code == dictionary.Count)
                {
                    using var newEntry = new ByteSequenceBuilder(previousEntry.Length + 1);
                    newEntry.Append(previousEntry.ToByteSequence());
                    newEntry.Append(previousEntry[0]);

                    var newEntrySequence = newEntry.ToByteSequence();
                    output.Append(newEntrySequence);
                    dictionary.Add(code, newEntrySequence);
                    previousEntry.Clear();
                    previousEntry.Append(newEntrySequence);
                }
                else
                {
                    throw new ArgumentException($"Bad compressed code: {code}");
                }
            }

            byte[] result = output.ToArray();

            foreach (var entry in dictionary.Values)
            {
                entry.Dispose();
            }

            return result;
        }
    }

    internal readonly struct ByteSequence : IEquatable<ByteSequence>, IDisposable
    {
        public readonly byte[] Array;
        public readonly int Length;
        private readonly int _hashCode;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public ByteSequence(byte[] array, int length)
        {
            Array = array;
            Length = length;

            _hashCode = 0;
            for (int i = 0; i < length; i++)
            {
                _hashCode = (_hashCode * 31) ^ array[i];
            }
        }

        public byte this[int index] => Array[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public bool Equals(ByteSequence other)
        {
            if (Length != other.Length || _hashCode != other._hashCode)
                return false;

            for (int i = 0; i < Length; i++)
            {
                if (Array[i] != other.Array[i])
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is ByteSequence other && Equals(other);

        public override int GetHashCode() => _hashCode;

        public void Dispose()
        {
            if (Array != null)
            {
                ArrayPool<byte>.Shared.Return(Array);
            }
        }
    }

    internal sealed class ByteSequenceEqualityComparer : IEqualityComparer<ByteSequence>
    {
        public static readonly ByteSequenceEqualityComparer Instance = new ByteSequenceEqualityComparer();

        public bool Equals(ByteSequence x, ByteSequence y) => x.Equals(y);

        public int GetHashCode(ByteSequence obj) => obj.GetHashCode();
    }

    internal sealed class ByteSequenceBuilder : IDisposable
    {
        private byte[] _buffer;
        private int _length;

        public int Length => _length;

        public ByteSequenceBuilder(int initialCapacity = 16)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Append(byte b)
        {
            if (_length == _buffer.Length)
            {
                var newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length << 1);
                System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }

            _buffer[_length++] = b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Append(ByteSequence sequence)
        {
            if (_length + sequence.Length > _buffer.Length)
            {
                int newSize = Math.Max(_buffer.Length * 2, _length + sequence.Length);
                var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _length);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }

            System.Buffer.BlockCopy(sequence.Array, 0, _buffer, _length, sequence.Length);
            _length += sequence.Length;
        }

        public void Clear()
        {
            _length = 0;
        }

        public byte this[int index] => _buffer[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public ByteSequence ToByteSequence()
        {
            var newArray = ArrayPool<byte>.Shared.Rent(_length);
            System.Buffer.BlockCopy(_buffer, 0, newArray, 0, _length);
            return new ByteSequence(newArray, _length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public ByteSequence GetSubSequence(int start, int length)
        {
            var newArray = ArrayPool<byte>.Shared.Rent(length);
            System.Buffer.BlockCopy(_buffer, start, newArray, 0, length);
            return new ByteSequence(newArray, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public byte[] ToArray()
        {
            var result = new byte[_length];
            System.Buffer.BlockCopy(_buffer, 0, result, 0, _length);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }
        }
    }
}
