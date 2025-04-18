using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tests
{
    //public static class SuffixArrayGenerator
    //{
    //    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    //    private static int UnsafeCompare(string s, int i, int j)
    //    {
    //        int len = s.Length;
    //        ref char r = ref MemoryMarshal.GetReference(s.AsSpan());

    //        while (i < len && j < len)
    //        {
    //            char a = Unsafe.Add(ref r, i);
    //            char b = Unsafe.Add(ref r, j);
    //            if (a != b)
    //                return a - b;
    //            i++;
    //            j++;
    //        }

    //        return (len - i) - (len - j); // shorter suffix comes first
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    //    public static int[] BuildSuffixArray(string input)
    //    {
    //        int length = input.Length;
    //        int[] suffixIndices = new int[length];
    //        for (int i = 0; i < length; i++)
    //            suffixIndices[i] = i;

    //        Array.Sort(suffixIndices, (i, j) => UnsafeCompare(input, i, j));
    //        return suffixIndices;
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    //    public static void PrintSuffixArray(string input)
    //    {
    //        var suffixArray = BuildSuffixArray(input);
    //        //foreach (int index in suffixArray)
    //        //{
    //        //    Console.WriteLine($"{index}: {input.Substring(index)}");
    //        //}
    //    }
    //}

    public static class SuffixArrayGenerator
    {
        private unsafe struct BytePointerSuffixComparer : IComparer<int>
        {
            private readonly byte* _ptr;
            private readonly int _length;

            public BytePointerSuffixComparer(byte* ptr, int length)
            {
                _ptr = ptr;
                _length = length;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public int Compare(int i, int j)
            {
                int max = _length - Math.Max(i, j);
                int offset = 0;

                while (offset + 8 <= max)
                {
                    ulong a = *(ulong*)(_ptr + i + offset);
                    ulong b = *(ulong*)(_ptr + j + offset);
                    if (a != b)
                        return a < b ? -1 : 1;
                    offset += 8;
                }

                while (offset < max)
                {
                    byte a = *(_ptr + i + offset);
                    byte b = *(_ptr + j + offset);
                    if (a != b)
                        return a - b;
                    offset++;
                }

                return (_length - i) - (_length - j); // shorter suffix first
            }
        }

        public unsafe static int[] BuildSuffixArray(string input)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(input); // Use Latin1 if you want 1:1 char->byte
            int length = buffer.Length;
            int[] suffixIndices = new int[length];

            for (int i = 0; i < length; i++)
                suffixIndices[i] = i;

            fixed (byte* ptr = buffer)
            {
                var comparer = new BytePointerSuffixComparer(ptr, length);
                Array.Sort(suffixIndices, comparer);
            }

            return suffixIndices;
        }

        public static void PrintSuffixArray(string input)
        {
            var sa = BuildSuffixArray(input);
            for (int i = 0; i < sa.Length; i++)
            {
                Console.WriteLine($"{sa[i]}: {input[sa[i]..]}");
            }
        }
    }
}
