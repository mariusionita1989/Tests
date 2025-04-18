using System.Buffers;
using System.Runtime.CompilerServices;


namespace Tests
{
    [SkipLocalsInit]
    public static class BWT
    {
        private const int MaxStackallocLength = 512;
        private const int AlphabetSize = 16;
        private const int AlphabetMask = AlphabetSize - 1;
        private const int LengthMultiplierShift = 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe (char[] bwt, int primaryIndex) Compress(string input)
        {
            int length = input.Length;
            int doubledLength = length << LengthMultiplierShift;
            bool useStackalloc = length <= MaxStackallocLength;

            byte[]? doubledArray = null;
            int[]? rotationArray = null;

            Span<byte> doubled = useStackalloc
                ? stackalloc byte[doubledLength]
                : (doubledArray = ArrayPool<byte>.Shared.Rent(doubledLength));

            Span<int> rotation = useStackalloc
                ? stackalloc int[length]
                : (rotationArray = ArrayPool<int>.Shared.Rent(length));

            for (int i = 0; i < length; i++)
            {
                byte b = (byte)(input[i] - 'A');
                doubled[i] = b;
                doubled[i + length] = b;
                rotation[i] = i;
            }

            byte[] doubledBuffer;
            if (!useStackalloc)
            {
                doubledBuffer = doubledArray!;
            }
            else
            {
                doubledBuffer = doubled.ToArray();
            }

            rotation.Sort(new RotationComparer(doubledBuffer, length));

            char[] bwtResult = new char[length];
            int primaryIndex = -1;

            for (int i = 0; i < length; i++)
            {
                int idx = rotation[i];
                if (idx == 0) primaryIndex = i;
                bwtResult[i] = (char)(doubled[idx + length - 1] + 'A');
            }

            if (!useStackalloc)
            {
                ArrayPool<byte>.Shared.Return(doubledArray!);
                ArrayPool<int>.Shared.Return(rotationArray!);
            }

            return (bwtResult, primaryIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static string Decompress(char[] bwt, int primaryIndex)
        {
            int length = bwt.Length;
            Span<int> count = stackalloc int[AlphabetSize];
            Span<int> start = stackalloc int[AlphabetSize];

            bool useStackalloc = length <= MaxStackallocLength;
            int[]? ranksArray = null;
            int[]? sortedArray = null;

            Span<int> ranks = useStackalloc
                ? stackalloc int[length]
                : (ranksArray = ArrayPool<int>.Shared.Rent(length));

            Span<int> sortedIndex = useStackalloc
                ? stackalloc int[length]
                : (sortedArray = ArrayPool<int>.Shared.Rent(length));

            for (int i = 0; i < length; i++)
            {
                int b = bwt[i] - 'A';
                ranks[i] = count[b]++;
            }

            for (int i = 1; i < AlphabetSize; i++)
                start[i] = start[i - 1] + count[i - 1];

            for (int i = 0; i < length; i++)
            {
                int b = bwt[i] - 'A';
                sortedIndex[start[b] + ranks[i]] = i;
            }

            var result = new char[length];
            int idx = primaryIndex;
            for (int i = 0; i < length; i++)
            {
                result[i] = bwt[idx];
                idx = sortedIndex[idx];
            }

            if (!useStackalloc)
            {
                ArrayPool<int>.Shared.Return(ranksArray!);
                ArrayPool<int>.Shared.Return(sortedArray!);
            }

            return new string(result);
        }

        private readonly unsafe struct RotationComparer : IComparer<int>
        {
            private readonly byte[] doubled;
            private readonly int length;

            public RotationComparer(byte[] doubled, int length)
            {
                this.doubled = doubled;
                this.length = length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int Compare(int x, int y)
            {
                fixed (byte* ptr = doubled)
                {
                    byte* a = ptr + x;
                    byte* b = ptr + y;

                    int i = 0;
                    for (; i + sizeof(ulong) <= length; i += sizeof(ulong))
                    {
                        ulong av = *(ulong*)(a + i);
                        ulong bv = *(ulong*)(b + i);
                        if (av != bv)
                            return av < bv ? -1 : 1;
                    }

                    for (; i < length; i++)
                    {
                        byte av = *(a + i);
                        byte bv = *(b + i);
                        if (av != bv)
                            return av - bv;
                    }

                    return 0;
                }
            }
        }
    }
}
