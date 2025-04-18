using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Tests
{
    public static class FastComparer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int Compare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            ref char aRef = ref MemoryMarshal.GetArrayDataReference(a);
            ref char bRef = ref MemoryMarshal.GetArrayDataReference(b);

            for (int i = 0; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb)
                    return ca - cb;
            }

            return a.Length - b.Length;
        }

        [DllImport("msvcrt.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int memcmp(IntPtr b1, IntPtr b2, UIntPtr count);


        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int UnsafeMemcmpCallCompare(char[] a, char[] b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int byteCount = minLength * sizeof(char); // 2 bytes per char

            fixed (char* ptrA = a)
            fixed (char* ptrB = b)
            {
                byte* bytePtrA = (byte*)ptrA;
                byte* bytePtrB = (byte*)ptrB;

                int result = memcmp((IntPtr)bytePtrA, (IntPtr)bytePtrB, (UIntPtr)byteCount);
                if (result != 0)
                    return result;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int Compare(string a, string b)
        {
            int length = Math.Min(a.Length, b.Length);
            ref char aRef = ref MemoryMarshal.GetReference(a.AsSpan());
            ref char bRef = ref MemoryMarshal.GetReference(b.AsSpan());

            for (int i = 0; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb)
                    return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            return a.SequenceCompareTo(b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareBytesFast(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int len = Math.Min(a.Length, b.Length);
            int i = 0;

            var ulongCount = len / sizeof(ulong);
            var aUlong = MemoryMarshal.Cast<byte, ulong>(a);
            var bUlong = MemoryMarshal.Cast<byte, ulong>(b);

            for (; i < ulongCount; i++)
            {
                ulong va = aUlong[i];
                ulong vb = bUlong[i];
                if (va != vb)
                {
                    // find first differing byte inside ulong
                    ulong diff = va ^ vb;
                    int shift = BitOperations.TrailingZeroCount(diff) & ~0x7;
                    return ((int)(va >> shift) & 0xFF) - ((int)(vb >> shift) & 0xFF);
                }
            }

            // Compare remaining bytes
            i *= sizeof(ulong);
            for (; i < len; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int MemCmp(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int len = Math.Min(a.Length, b.Length);
            for (int i = 0; i < len; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int MemCmpOptimized(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int len = Math.Min(a.Length, b.Length);
            int i = 0;

            var ulongCount = len / sizeof(ulong);
            var aUlong = MemoryMarshal.Cast<byte, ulong>(a);
            var bUlong = MemoryMarshal.Cast<byte, ulong>(b);

            for (; i < ulongCount; i++)
            {
                ulong va = aUlong[i];
                ulong vb = bUlong[i];
                if (va != vb)
                {
                    // find first differing byte inside ulong
                    ulong diff = va ^ vb;
                    int shift = BitOperations.TrailingZeroCount(diff) & ~0x7;
                    return ((int)(va >> shift) & 0xFF) - ((int)(vb >> shift) & 0xFF);
                }
            }

            // Compare remaining bytes (after ulong chunks)
            i *= sizeof(ulong);
            for (; i < len; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareHalfSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int half = minLength>>1;

            // Compare first half
            int firstHalfResult = a.Slice(0, half).SequenceCompareTo(b.Slice(0, half));
            if (firstHalfResult != 0)
                return firstHalfResult;

            // Compare second half
            int secondHalfLength = minLength - half;
            int secondHalfResult = a.Slice(half, secondHalfLength).SequenceCompareTo(b.Slice(half, secondHalfLength));
            if (secondHalfResult != 0)
                return secondHalfResult;

            // If all matched, compare lengths
            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareQuarterSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int quarter = minLength>>2;
            int half = minLength>>1;
            int three_quarters = minLength*3;


            // Compare 1st quarter
            for (int i = 0; i < quarter; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 2nd quarter
            for (int i = quarter; i < half; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 3rd quarter
            for (int i = half; i < three_quarters; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 4th quarter (and any remaining bytes)
            for (int i = three_quarters; i < minLength; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareEighthSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int eighth = minLength >> 3;

            // Compute segment boundaries
            int one = eighth;
            int two = eighth<<1;
            int three = eighth*3;
            int four = eighth<<2;
            int five = eighth*5;
            int six = eighth*6;
            int seven = eighth*7;

            // Compare 1st eighth
            for (int i = 0; i < one; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 2nd eighth
            for (int i = one; i < two; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 3rd eighth
            for (int i = two; i < three; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 4th eighth
            for (int i = three; i < four; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 5th eighth
            for (int i = four; i < five; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 6th eighth
            for (int i = five; i < six; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 7th eighth
            for (int i = six; i < seven; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Compare 8th eighth (plus remaining bytes)
            for (int i = seven; i < minLength; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Fallback to length difference
            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareSixteenthSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int sixteenth = minLength >> 4; // equivalent to minLength / 16

            // Compare 1st to 15th sixteenths
            for (int chunk = 0; chunk < 15; chunk++)
            {
                int start = chunk * sixteenth;
                int end = start + sixteenth;

                for (int i = start; i < end; i++)
                {
                    int diff = a[i] - b[i];
                    if (diff != 0) return diff;
                }
            }

            // Compare 16th sixteenth + any remaining bytes
            int lastStart = sixteenth * 15;
            for (int i = lastStart; i < minLength; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0) return diff;
            }

            // Fallback to length difference
            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareThirtySecondSpans(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int chunkSize = minLength >> 5; // minLength / 32

            int chunk = 0;
            int start = 0;

            // Compare chunks 0..30 (first 31 chunks)
            while (chunk < 31)
            {
                int end = start + chunkSize;

                for (int i = start; i < end; i++)
                {
                    int diff = a[i] - b[i];
                    if (diff != 0)
                        return diff > 0 ? 1 : -1;
                }

                start = end;
                chunk++;
            }

            // Compare last chunk + remaining bytes
            for (int i = start; i < minLength; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0)
                    return diff > 0 ? 1 : -1;
            }

            // Final comparison if all equal so far
            return a.Length == b.Length ? 0 : (a.Length > b.Length ? 1 : -1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareSpansUlong(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int i = 0;

            // Compare in 8-byte chunks
            while (i <= minLength - sizeof(ulong))
            {
                ulong va = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(a), i));
                ulong vb = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref MemoryMarshal.GetReference(b), i));

                if (va != vb)
                {
                    // Compare byte by byte within the differing ulong
                    for (int j = 0; j < sizeof(ulong); j++)
                    {
                        byte ba = (byte)(va >> (j<<3));
                        byte bb = (byte)(vb >> (j<<3));
                        if (ba != bb)
                            return ba > bb ? 1 : -1;
                    }
                }

                i += sizeof(ulong);
            }

            // Compare remaining bytes
            for (; i < minLength; i++)
            {
                int diff = a[i] - b[i];
                if (diff != 0)
                    return diff > 0 ? 1 : -1;
            }

            return a.Length == b.Length ? 0 : (a.Length > b.Length ? 1 : -1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareSpansUlongFast(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int minLength = Math.Min(a.Length, b.Length);
            int i = 0;
            ref byte ra = ref MemoryMarshal.GetReference(a);
            ref byte rb = ref MemoryMarshal.GetReference(b);

            while (i <= minLength - sizeof(ulong))
            {
                ulong va = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref ra, i));
                ulong vb = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref rb, i));
                if (va != vb)
                    return va > vb ? 1 : -1;
                i += sizeof(ulong);
            }

            // Remaining bytes
            for (; i < minLength; i++)
            {
                byte ba = Unsafe.Add(ref ra, i);
                byte bb = Unsafe.Add(ref rb, i);
                if (ba != bb)
                    return ba > bb ? 1 : -1;
            }

            return a.Length == b.Length ? 0 : (a.Length > b.Length ? 1 : -1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int UltraFastUlongCompare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            int len = Math.Min(a.Length, b.Length);
            int ulongCount = len / sizeof(ulong);
            int i = 0;

            fixed (byte* pa = a, pb = b)
            {
                ulong* ua = (ulong*)pa;
                ulong* ub = (ulong*)pb;

                // Unroll the loop by 4 for better performance
                for (; i <= ulongCount - 4; i += 4)
                {
                    ulong va0 = ua[i];
                    ulong vb0 = ub[i];
                    if (va0 != vb0) return va0 < vb0 ? -1 : 1;

                    ulong va1 = ua[i + 1];
                    ulong vb1 = ub[i + 1];
                    if (va1 != vb1) return va1 < vb1 ? -1 : 1;

                    ulong va2 = ua[i + 2];
                    ulong vb2 = ub[i + 2];
                    if (va2 != vb2) return va2 < vb2 ? -1 : 1;

                    ulong va3 = ua[i + 3];
                    ulong vb3 = ub[i + 3];
                    if (va3 != vb3) return va3 < vb3 ? -1 : 1;
                }

                // Remaining ulong comparisons
                for (; i < ulongCount; i++)
                {
                    ulong va = ua[i];
                    ulong vb = ub[i];
                    if (va != vb) return va < vb ? -1 : 1;
                }

                // Handle tail bytes
                int byteIndex = ulongCount * sizeof(ulong);
                for (; byteIndex < len; byteIndex++)
                {
                    byte ba = pa[byteIndex];
                    byte bb = pb[byteIndex];
                    if (ba != bb) return ba < bb ? -1 : 1;
                }
            }

            return a.Length.CompareTo(b.Length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CompareUnrolled(string a, string b)
        {
            int length = Math.Min(a.Length, b.Length);
            int i = 0;

            ref char aRef = ref MemoryMarshal.GetReference(a.AsSpan());
            ref char bRef = ref MemoryMarshal.GetReference(b.AsSpan());

            int unrollLimit = length - 3;
            for (; i <= unrollLimit; i += 4)
            {
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                if (ca0 != cb0) return ca0 - cb0;

                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                if (ca1 != cb1) return ca1 - cb1;

                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                if (ca2 != cb2) return ca2 - cb2;

                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);
                if (ca3 != cb3) return ca3 - cb3;
            }

            // handle tail
            for (; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb) return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int CompareUnsafe(string a, string b)
        {
            int len = Math.Min(a.Length, b.Length);
            fixed (char* aPtr = a)
            fixed (char* bPtr = b)
            {
                for (int i = 0; i < len; i++)
                {
                    char ca = aPtr[i];
                    char cb = bPtr[i];
                    if (ca != cb)
                        return ca - cb;
                }
            }
            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CharStringCompare(char[] a, char[] b)
        {
            int len = (a.Length < b.Length) ? a.Length : b.Length;
            for (int i = 0; i < len; i++)
            {
                char ca = a[i];
                char cb = b[i];
                if (ca != cb)
                    return ca - cb;
            }
            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int UnsafeUnrolledForLoopCompare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            ref char aRef = ref MemoryMarshal.GetArrayDataReference(a);
            ref char bRef = ref MemoryMarshal.GetArrayDataReference(b);

            int i = 0;
            // Process 4 chars at a time (loop unrolling)
            for (; i + 3 < length; i += 4)
            {
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                if (ca0 != cb0) return ca0 - cb0;

                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                if (ca1 != cb1) return ca1 - cb1;

                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                if (ca2 != cb2) return ca2 - cb2;

                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);
                if (ca3 != cb3) return ca3 - cb3;
            }

            // Process remaining elements
            for (; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb) return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int OptimizedPointerCompare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);

            fixed (char* aPtr = a, bPtr = b)
            {
                char* pa = aPtr;
                char* pb = bPtr;
                char* end = pa + length;

                while (pa < end)
                {
                    if (*pa != *pb)
                        return *pa - *pb;
                    pa++;
                    pb++;
                }
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SIMDFastCompare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            int i = 0;

            if (Vector.IsHardwareAccelerated && length >= Vector<ushort>.Count)
            {
                int vectorSize = Vector<ushort>.Count;
                int lastBlock = length - vectorSize;

                fixed (char* aPtr = a, bPtr = b)
                {
                    for (; i <= lastBlock; i += vectorSize)
                    {
                        var va = new Vector<ushort>((ushort)(aPtr + i));
                        var vb = new Vector<ushort>((ushort)(bPtr + i));

                        if (!Vector.EqualsAll(va, vb))
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + j];
                                char cb = bPtr[i + j];
                                if (ca != cb) return ca - cb;
                            }
                        }
                    }
                }
            }

            // Process remaining elements
            for (; i < length; i++)
            {
                char ca = a[i];
                char cb = b[i];
                if (ca != cb) return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SIMDOptimizedCompare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            int i = 0;

            if (Vector.IsHardwareAccelerated && length >= Vector<ushort>.Count<<1)
            {
                int vectorSize = Vector<ushort>.Count;
                int lastBlock = length - (vectorSize<<1);

                fixed (char* aPtr = a, bPtr = b)
                {
                    // Process two vectors at a time to better utilize pipelining
                    for (; i <= lastBlock; i += vectorSize<<1)
                    {
                        var va1 = new Vector<ushort>((ushort)(aPtr + i));
                        var vb1 = new Vector<ushort>((ushort)(bPtr + i));
                        var va2 = new Vector<ushort>((ushort)(aPtr + i + vectorSize));
                        var vb2 = new Vector<ushort>((ushort)(bPtr + i + vectorSize));

                        if (Vector.Xor(va1, vb1) != Vector<ushort>.Zero)
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + j];
                                char cb = bPtr[i + j];
                                if (ca != cb) return ca - cb;
                            }
                        }

                        if (Vector.Xor(va2, vb2) != Vector<ushort>.Zero)
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + vectorSize + j];
                                char cb = bPtr[i + vectorSize + j];
                                if (ca != cb) return ca - cb;
                            }
                        }
                    }
                }
            }

            // Process remaining elements with unrolled loop
            ref char aRef = ref MemoryMarshal.GetArrayDataReference(a);
            ref char bRef = ref MemoryMarshal.GetArrayDataReference(b);

            for (; i + 3 < length; i += 4)
            {
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                if (ca0 != cb0) return ca0 - cb0;

                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                if (ca1 != cb1) return ca1 - cb1;

                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                if (ca2 != cb2) return ca2 - cb2;

                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);
                if (ca3 != cb3) return ca3 - cb3;
            }

            for (; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb) return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SIMDFasterCompare(char[] a, char[] b)
        {
            int length = Math.Min(a.Length, b.Length);
            int i = 0;

            if (Avx2.IsSupported && length >= Vector256<ushort>.Count << 1)
            {
                int vectorSize = Vector256<ushort>.Count;
                int lastBlock = length - (vectorSize << 1);

                fixed (char* aPtr = a, bPtr = b)
                {
                    // Process two 256-bit vectors at a time
                    for (; i <= lastBlock; i += vectorSize << 1)
                    {
                        var va1 = Avx2.LoadVector256((ushort*)(aPtr + i));
                        var vb1 = Avx2.LoadVector256((ushort*)(bPtr + i));
                        var va2 = Avx2.LoadVector256((ushort*)(aPtr + i + vectorSize));
                        var vb2 = Avx2.LoadVector256((ushort*)(bPtr + i + vectorSize));

                        var ne1 = Avx2.CompareEqual(va1, vb1);
                        if (Avx2.MoveMask(ne1.AsByte()) != -1)  // -1 means all equal
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + j];
                                char cb = bPtr[i + j];
                                if (ca != cb) return ca - cb;
                            }
                        }

                        var ne2 = Avx2.CompareEqual(va2, vb2);
                        if (Avx2.MoveMask(ne2.AsByte()) != -1)
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + vectorSize + j];
                                char cb = bPtr[i + vectorSize + j];
                                if (ca != cb) return ca - cb;
                            }
                        }
                    }
                }
            }
            else if (Vector.IsHardwareAccelerated && length >= Vector<ushort>.Count << 1)
            {
                // Fallback to Vector128 if AVX2 not available
                int vectorSize = Vector<ushort>.Count;
                int lastBlock = length - (vectorSize << 1);

                fixed (char* aPtr = a, bPtr = b)
                {
                    for (; i <= lastBlock; i += vectorSize << 1)
                    {
                        var va1 = new Vector<ushort>((ushort)(aPtr + i));
                        var vb1 = new Vector<ushort>((ushort)(bPtr + i));
                        var va2 = new Vector<ushort>((ushort)(aPtr + i + vectorSize));
                        var vb2 = new Vector<ushort>((ushort)(bPtr + i + vectorSize));

                        if (Vector.Xor(va1, vb1) != Vector<ushort>.Zero)
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + j];
                                char cb = bPtr[i + j];
                                if (ca != cb) return ca - cb;
                            }
                        }

                        if (Vector.Xor(va2, vb2) != Vector<ushort>.Zero)
                        {
                            for (int j = 0; j < vectorSize; j++)
                            {
                                char ca = aPtr[i + vectorSize + j];
                                char cb = bPtr[i + vectorSize + j];
                                if (ca != cb) return ca - cb;
                            }
                        }
                    }
                }
            }

            // Process remaining elements with aggressive unrolling
            ref char aRef = ref MemoryMarshal.GetArrayDataReference(a);
            ref char bRef = ref MemoryMarshal.GetArrayDataReference(b);

            // 8x unrolling for remaining elements
            for (; i + 7 < length; i += 8)
            {
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                if (ca0 != cb0) return ca0 - cb0;

                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                if (ca1 != cb1) return ca1 - cb1;

                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                if (ca2 != cb2) return ca2 - cb2;

                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);
                if (ca3 != cb3) return ca3 - cb3;

                char ca4 = Unsafe.Add(ref aRef, i + 4);
                char cb4 = Unsafe.Add(ref bRef, i + 4);
                if (ca4 != cb4) return ca4 - cb4;

                char ca5 = Unsafe.Add(ref aRef, i + 5);
                char cb5 = Unsafe.Add(ref bRef, i + 5);
                if (ca5 != cb5) return ca5 - cb5;

                char ca6 = Unsafe.Add(ref aRef, i + 6);
                char cb6 = Unsafe.Add(ref bRef, i + 6);
                if (ca6 != cb6) return ca6 - cb6;

                char ca7 = Unsafe.Add(ref aRef, i + 7);
                char cb7 = Unsafe.Add(ref bRef, i + 7);
                if (ca7 != cb7) return ca7 - cb7;
            }

            // Handle any remaining elements (0-7)
            for (; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb) return ca - cb;
            }

            return a.Length - b.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SIMDEvenFasterCompare(char[] a, char[] b)
        {
            // Early exit for length differences
            if (a.Length != b.Length)
                return a.Length - b.Length;
            if (a.Length == 0)
                return 0;

            int i = 0;
            int length = a.Length;

            if (Avx2.IsSupported)
            {
                const int vectorSize = 16; // 256 bits / 16 bits per char
                if (length >= vectorSize << 1)
                {
                    int lastBlock = length - (vectorSize << 1);
                    fixed (char* aPtr = a, bPtr = b)
                    {
                        // Main SIMD processing loop
                        while (i <= lastBlock)
                        {
                            // Load and compare first vector
                            var va = Avx2.LoadVector256((ushort*)(aPtr + i));
                            var vb = Avx2.LoadVector256((ushort*)(bPtr + i));
                            var neq = Avx2.CompareEqual(va, vb);
                            if (Avx2.MoveMask(neq.AsByte()) != -1)
                            {
                                for (int j = 0; j < vectorSize; j++)
                                {
                                    if (aPtr[i + j] != bPtr[i + j])
                                        return aPtr[i + j] - bPtr[i + j];
                                }
                            }
                            i += vectorSize;

                            // Load and compare second vector
                            va = Avx2.LoadVector256((ushort*)(aPtr + i));
                            vb = Avx2.LoadVector256((ushort*)(bPtr + i));
                            neq = Avx2.CompareEqual(va, vb);
                            if (Avx2.MoveMask(neq.AsByte()) != -1)
                            {
                                for (int j = 0; j < vectorSize; j++)
                                {
                                    if (aPtr[i + j] != bPtr[i + j])
                                        return aPtr[i + j] - bPtr[i + j];
                                }
                            }
                            i += vectorSize;
                        }
                    }
                }
            }

            // Process remaining elements (0-31 chars)
            if (i + 15 < length && Vector.IsHardwareAccelerated)
            {
                const int vectorSize = 8; // 128 bits / 16 bits per char
                fixed (char* aPtr = a, bPtr = b)
                {
                    var va = new Vector<ushort>((ushort)(aPtr + i));
                    var vb = new Vector<ushort>((ushort)(bPtr + i));
                    if (Vector.Xor(va, vb) != Vector<ushort>.Zero)
                    {
                        for (int j = 0; j < vectorSize; j++)
                        {
                            if (aPtr[i + j] != bPtr[i + j])
                                return aPtr[i + j] - bPtr[i + j];
                        }
                    }
                    i += vectorSize;
                }
            }

            // Ultra-fast scalar processing for the last 0-15 chars
            fixed (char* aPtr = a, bPtr = b)
            {
                while (i + 7 < length)
                {
                    if (aPtr[i] != bPtr[i]) return aPtr[i] - bPtr[i];
                    if (aPtr[i + 1] != bPtr[i + 1]) return aPtr[i + 1] - bPtr[i + 1];
                    if (aPtr[i + 2] != bPtr[i + 2]) return aPtr[i + 2] - bPtr[i + 2];
                    if (aPtr[i + 3] != bPtr[i + 3]) return aPtr[i + 3] - bPtr[i + 3];
                    if (aPtr[i + 4] != bPtr[i + 4]) return aPtr[i + 4] - bPtr[i + 4];
                    if (aPtr[i + 5] != bPtr[i + 5]) return aPtr[i + 5] - bPtr[i + 5];
                    if (aPtr[i + 6] != bPtr[i + 6]) return aPtr[i + 6] - bPtr[i + 6];
                    if (aPtr[i + 7] != bPtr[i + 7]) return aPtr[i + 7] - bPtr[i + 7];
                    i += 8;
                }

                while (i < length)
                {
                    if (aPtr[i] != bPtr[i]) return aPtr[i] - bPtr[i];
                    i++;
                }
            }

            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int FastUnsafeUnrolledForLoopCompare(char[] a, char[] b)
        {
            // Early length check for quick exit
            if (a.Length != b.Length)
                return a.Length - b.Length;
            if (a.Length == 0)
                return 0;

            ref char aRef = ref MemoryMarshal.GetArrayDataReference(a);
            ref char bRef = ref MemoryMarshal.GetArrayDataReference(b);
            int length = a.Length;
            int i = 0;

            // 8-way unrolled main loop
            for (; i + 7 < length; i += 8)
            {
                // Read all 8 pairs before comparing to allow better instruction scheduling
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);
                char ca4 = Unsafe.Add(ref aRef, i + 4);
                char cb4 = Unsafe.Add(ref bRef, i + 4);
                char ca5 = Unsafe.Add(ref aRef, i + 5);
                char cb5 = Unsafe.Add(ref bRef, i + 5);
                char ca6 = Unsafe.Add(ref aRef, i + 6);
                char cb6 = Unsafe.Add(ref bRef, i + 6);
                char ca7 = Unsafe.Add(ref aRef, i + 7);
                char cb7 = Unsafe.Add(ref bRef, i + 7);

                // Compare in reverse order to potentially exit early
                if (ca7 != cb7) return ca7 - cb7;
                if (ca6 != cb6) return ca6 - cb6;
                if (ca5 != cb5) return ca5 - cb5;
                if (ca4 != cb4) return ca4 - cb4;
                if (ca3 != cb3) return ca3 - cb3;
                if (ca2 != cb2) return ca2 - cb2;
                if (ca1 != cb1) return ca1 - cb1;
                if (ca0 != cb0) return ca0 - cb0;
            }

            // 4-way unrolled for remaining 4-7 elements
            if (i + 3 < length)
            {
                char ca0 = Unsafe.Add(ref aRef, i);
                char cb0 = Unsafe.Add(ref bRef, i);
                char ca1 = Unsafe.Add(ref aRef, i + 1);
                char cb1 = Unsafe.Add(ref bRef, i + 1);
                char ca2 = Unsafe.Add(ref aRef, i + 2);
                char cb2 = Unsafe.Add(ref bRef, i + 2);
                char ca3 = Unsafe.Add(ref aRef, i + 3);
                char cb3 = Unsafe.Add(ref bRef, i + 3);

                if (ca3 != cb3) return ca3 - cb3;
                if (ca2 != cb2) return ca2 - cb2;
                if (ca1 != cb1) return ca1 - cb1;
                if (ca0 != cb0) return ca0 - cb0;
                i += 4;
            }

            // Final elements (0-3)
            for (; i < length; i++)
            {
                char ca = Unsafe.Add(ref aRef, i);
                char cb = Unsafe.Add(ref bRef, i);
                if (ca != cb) return ca - cb;
            }

            return 0;
        }
    }
}
