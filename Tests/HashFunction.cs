using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Tests
{
    public static class HashFunction
    {
        private const uint FnvPrime = 0x01000193;
        private const uint FnvOffsetBasis = 0x811C9DC5;

        private const ulong Fnv64OffsetBasis = 14695981039346656037UL;
        private const ulong Fnv64Prime = 1099511628211UL;

        #region Naive Hash Function
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static int CalculateHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            var span = source.Slice(start, length);
            var bytes = MemoryMarshal.AsBytes(span);
            unchecked
            {
                int hash = 5381;
                foreach (byte b in bytes)
                    hash = ((hash << 5) + hash) ^ b;
                return hash;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int UnsafeCalculateHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            // Early exit for empty input
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* ptr = &MemoryMarshal.GetReference(source.Slice(start, length)))
            {
                byte* data = ptr;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    // Process 8 bytes at a time (64-bit chunks)
                    while (data + 8 <= end)
                    {
                        // Single memory read for 8 bytes
                        ulong chunk = *(ulong*)data;

                        // Fully unrolled processing
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 24) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 32) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 40) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 48) & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 56));

                        data += 8;
                    }

                    // Process remaining 4 bytes if available
                    if (data + 4 <= end)
                    {
                        uint chunk = *(uint*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 24));
                        data += 4;
                    }

                    // Process remaining 2 bytes if available
                    if (data + 2 <= end)
                    {
                        ushort chunk = *(ushort*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 8));
                        data += 2;
                    }

                    // Process last byte if needed
                    if (data < end)
                    {
                        hash = ((hash * 33) ^ *data);
                    }

                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SimdCalculateHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* ptr = &MemoryMarshal.GetReference(source.Slice(start, length)))
            {
                byte* data = ptr;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    // Vectorized processing when available
                    if (Avx2.IsSupported && length >= 32)
                    {
                        var vhash = Vector256.Create(hash);
                        var v33 = Vector256.Create(33);

                        while (data + 32 <= end)
                        {
                            var vector = Avx2.ConvertToVector256Int32(data);

                            // Process each 32-bit chunk (contains 4 bytes)
                            for (int i = 0; i < 8; i++)  // 8x32-bit = 32 bytes
                            {
                                var chunk = vector.GetElement(i);
                                vhash = Avx2.MultiplyLow(vhash.AsInt16(), v33.AsInt16()).AsInt32();
                                vhash = Avx2.Xor(vhash, Vector256.Create(chunk & 0xFF));
                                vhash = Avx2.Xor(vhash, Vector256.Create((chunk >> 8) & 0xFF));
                                vhash = Avx2.Xor(vhash, Vector256.Create((chunk >> 16) & 0xFF));
                                vhash = Avx2.Xor(vhash, Vector256.Create(chunk >> 24));
                            }

                            data += 32;
                        }

                        // Reduce vector hash to scalar
                        hash = vhash.GetElement(0);
                        for (int i = 1; i < 8; i++)
                            hash = (hash * 33) ^ vhash.GetElement(i);
                    }
                    else if (Sse2.IsSupported && length >= 16)
                    {
                        var vhash = Vector128.Create(hash);
                        var v33 = Vector128.Create(33);

                        while (data + 16 <= end)
                        {
                            var vector = Sse2.LoadVector128(data);

                            // Process each byte in the vector
                            for (int i = 0; i < 16; i++)
                            {
                                var byteVal = (int)vector.GetElement(i);
                                vhash = Sse2.MultiplyLow(vhash.AsInt16(), v33.AsInt16()).AsInt32();
                                vhash = Sse2.Xor(vhash, Vector128.Create(byteVal));
                            }

                            data += 16;
                        }

                        // Reduce vector hash to scalar
                        hash = vhash.GetElement(0);
                        for (int i = 1; i < 4; i++)
                            hash = (hash * 33) ^ vhash.GetElement(i);
                    }

                    // Process remaining 8-byte chunks
                    while (data + 8 <= end)
                    {
                        ulong chunk = *(ulong*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 24) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 32) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 40) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 48) & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 56));
                        data += 8;
                    }

                    // Process remaining 4 bytes
                    if (data + 4 <= end)
                    {
                        uint chunk = *(uint*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 24));
                        data += 4;
                    }

                    // Process remaining 2 bytes
                    if (data + 2 <= end)
                    {
                        ushort chunk = *(ushort*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 8));
                        data += 2;
                    }

                    // Process final byte
                    if (data < end)
                    {
                        hash = ((hash * 33) ^ *data);
                    }

                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int UltraFastHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* ptr = &MemoryMarshal.GetReference(source.Slice(start, length)))
            {
                byte* data = ptr;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    // Specialized path for very small inputs
                    if (length <= 16)
                    {
                        ulong chunk = 0;
                        if (length >= 8)
                        {
                            chunk = *(ulong*)data;
                            data += 8;
                        }
                        else if (length >= 4)
                        {
                            chunk = *(uint*)data;
                            data += 4;
                        }

                        for (int i = 0; i < Math.Min(8, length); i++)
                        {
                            hash = (hash * 33) ^ (int)((chunk >> (i * 8)) & 0xFF);
                        }

                        while (data < end)
                        {
                            hash = (hash * 33) ^ *data++;
                        }
                        return hash;
                    }

                    // AVX2 optimized path (32 bytes at a time)
                    if (Avx2.IsSupported)
                    {
                        var vhash = Vector256.Create(hash);
                        var v33 = Vector256.Create(33);
                        var mask = Vector256.Create(0xFF);

                        while (data + 32 <= end)
                        {
                            var vector = Avx2.LoadVector256(data);

                            // Process bytes 0-7
                            var chunk0 = Avx2.ConvertToVector256Int32(data);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsInt32();
                            vhash = Avx2.Xor(vhash, Avx2.And(chunk0, mask));

                            // Process bytes 8-15
                            var chunk1 = Avx2.ConvertToVector256Int32(data + 8);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsInt32();
                            vhash = Avx2.Xor(vhash, Avx2.And(chunk1, mask));

                            // Process bytes 16-23
                            var chunk2 = Avx2.ConvertToVector256Int32(data + 16);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsInt32();
                            vhash = Avx2.Xor(vhash, Avx2.And(chunk2, mask));

                            // Process bytes 24-31
                            var chunk3 = Avx2.ConvertToVector256Int32(data + 24);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsInt32();
                            vhash = Avx2.Xor(vhash, Avx2.And(chunk3, mask));

                            data += 32;
                        }

                        // Horizontal reduction
                        var sum = vhash;
                        sum = Avx2.Xor(sum, Avx2.Shuffle(sum, 0x4E)); // Swap high and low
                        sum = Avx2.Xor(sum, Avx2.Shuffle(sum, 0xB1)); // Swap 32-bit pairs
                        hash = sum.GetElement(0) ^ sum.GetElement(4);
                    }

                    // Process remaining 16 bytes with SSE2 if available
                    if (Sse2.IsSupported && data + 16 <= end)
                    {
                        var vhash = Vector128.Create(hash);
                        var v33 = Vector128.Create(33);
                        var mask = Vector128.Create(0xFF);

                        var vector = Sse2.LoadVector128(data);
                        for (int i = 0; i < 16; i++)
                        {
                            vhash = Sse2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsInt32();
                            vhash = Sse2.Xor(vhash, Vector128.Create((int)vector.GetElement(i)));
                        }
                        data += 16;

                        // Horizontal reduction
                        hash = vhash.GetElement(0);
                        for (int i = 1; i < 4; i++)
                            hash = (hash * 33) ^ vhash.GetElement(i);
                    }

                    // Process remaining 8-byte chunks
                    while (data + 8 <= end)
                    {
                        ulong chunk = *(ulong*)data;
                        hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 24) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 32) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 40) & 0xFF));
                        hash = ((hash * 33) ^ (int)((chunk >> 48) & 0xFF));
                        hash = ((hash * 33) ^ (int)(chunk >> 56));
                        data += 8;
                    }

                    // Process remaining bytes
                    if (data < end)
                    {
                        if (data + 4 <= end)
                        {
                            uint chunk = *(uint*)data;
                            hash = ((hash * 33) ^ (int)(chunk & 0xFF));
                            hash = ((hash * 33) ^ (int)((chunk >> 8) & 0xFF));
                            hash = ((hash * 33) ^ (int)((chunk >> 16) & 0xFF));
                            hash = ((hash * 33) ^ (int)(chunk >> 24));
                            data += 4;
                        }

                        while (data < end)
                        {
                            hash = ((hash * 33) ^ *data++);
                        }
                    }

                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int SimdUltraFastHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* basePtr = source)
            {
                byte* data = basePtr + start;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    // Specialized path for very small inputs
                    if (length <= 16)
                    {
                        ulong chunk = 0;
                        if (length >= 8)
                        {
                            chunk = Unsafe.ReadUnaligned<ulong>(data);
                            data += 8;
                        }
                        else if (length >= 4)
                        {
                            chunk = Unsafe.ReadUnaligned<uint>(data);
                            data += 4;
                        }

                        int chunkLen = length >= 8 ? 8 : (length >= 4 ? 4 : length);
                        for (int i = 0; i < chunkLen; i++)
                            hash = (hash * 33) ^ (int)((chunk >> (i << 3)) & 0xFF);

                        while (data < end)
                            hash = (hash * 33) ^ *data++;

                        return hash;
                    }

                    // AVX2 optimized path
                    if (Avx2.IsSupported)
                    {
                        var v33 = Vector256.Create((byte)33);
                        Vector256<byte> vhash = Vector256<byte>.Zero;

                        while (data + 32 <= end)
                        {
                            var vector = Avx.LoadVector256(data);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsByte(); // Convert to ushort and back
                            vhash = Avx2.Xor(vhash, vector);
                            data += 32;
                        }

                        // Store to stackalloc and reduce
                        byte* buffer = stackalloc byte[32];
                        Avx.Store(buffer, vhash);
                        for (int i = 0; i < 32; i++)
                            hash = (hash * 33) ^ buffer[i];
                    }

                    // Process remaining 8-byte chunks
                    while (data + 8 <= end)
                    {
                        ulong chunk = Unsafe.ReadUnaligned<ulong>(data);
                        data += 8;
                        for (int i = 0; i < 8; i++)
                            hash = (hash * 33) ^ (int)((chunk >> (i << 3)) & 0xFF);
                    }

                    // Process remaining bytes
                    while (data < end)
                        hash = (hash * 33) ^ *data++;

                    return hash;
                }
            }
        }

        // the fastest
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int ImprovedSimdUltraFastHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* basePtr = source)
            {
                byte* data = basePtr + start;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    if (length <= 16)
                    {
                        ulong chunk = 0;
                        int chunkLen = 0;

                        if (length >= 8)
                        {
                            chunk = Unsafe.ReadUnaligned<ulong>(data);
                            data += 8;
                            chunkLen = 8;
                        }
                        else if (length >= 4)
                        {
                            chunk = Unsafe.ReadUnaligned<uint>(data);
                            data += 4;
                            chunkLen = 4;
                        }
                        else
                        {
                            chunkLen = length;
                        }

                        for (int i = 0; i < chunkLen; i++)
                            hash = (hash * 33) ^ (int)((chunk >> (i << 3)) & 0xFF); // i * 8 → i << 3

                        while (data < end)
                            hash = (hash * 33) ^ *data++;

                        return hash;
                    }

                    if (Avx2.IsSupported)
                    {
                        var v33 = Vector256.Create((byte)33);
                        Vector256<byte> vhash = Vector256<byte>.Zero;

                        while (data + 32 <= end)
                        {
                            var vector = Avx.LoadVector256(data);
                            vhash = Avx2.MultiplyLow(vhash.AsUInt16(), v33.AsUInt16()).AsByte();
                            vhash = Avx2.Xor(vhash, vector);
                            data += 32;
                        }

                        byte* buffer = stackalloc byte[32];
                        Avx.Store(buffer, vhash);
                        for (int i = 0; i < 32; i++)
                            hash = (hash * 33) ^ buffer[i];
                    }

                    while (data + 8 <= end)
                    {
                        ulong chunk = Unsafe.ReadUnaligned<ulong>(data);
                        data += 8;
                        for (int i = 0; i < 8; i++)
                            hash = (hash * 33) ^ (int)((chunk >> (i << 3)) & 0xFF); // i * 8 → i << 3
                    }

                    while (data < end)
                        hash = (hash * 33) ^ *data++;

                    return hash;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe int ImprovedFastHashCode(ReadOnlySpan<byte> source, int start, int length)
        {
            if (length <= 0 || source.IsEmpty)
                return 0;

            fixed (byte* basePtr = source)
            {
                byte* data = basePtr + start;
                byte* end = data + length;
                int hash = 5381;

                unchecked
                {
                    // Tiny input optimization (<= 16 bytes)
                    if (length <= 16)
                    {
                        // Process 8 bytes if available
                        if (length >= 8)
                        {
                            ulong chunk = Unsafe.ReadUnaligned<ulong>(data);
                            hash = (hash * 33 * 33 * 33 * 33 * 33 * 33 * 33 * 33)
                                   ^ (int)chunk
                                   ^ (int)(chunk >> 32);
                            data += 8;
                        }

                        // Process remaining 4 bytes if available
                        if (length >= 4 && (end - data) >= 4)
                        {
                            uint chunk = Unsafe.ReadUnaligned<uint>(data);
                            hash = (hash * 33 * 33 * 33 * 33) ^ (int)chunk;
                            data += 4;
                        }

                        // Process remaining bytes
                        while (data < end)
                        {
                            hash = (hash * 33) ^ *data++;
                        }
                        return hash;
                    }

                    // Stack allocated buffers for vector reduction
                    int* avxBuffer = stackalloc int[8];
                    int* sseBuffer = stackalloc int[4];

                    // AVX2 optimized path
                    if (Avx2.IsSupported)
                    {
                        Vector256<int> vhash = Vector256.Create(hash);
                        Vector256<int> v33_8 = Vector256.Create(33 * 33 * 33 * 33 * 33 * 33 * 33 * 33);

                        // Process 32-byte chunks
                        while (data + 32 <= end)
                        {
                            Vector256<int> vector = Avx2.LoadVector256(data).AsInt32();
                            vhash = Avx2.MultiplyLow(vhash, v33_8);
                            vhash = Avx2.Xor(vhash, vector);
                            data += 32;
                        }

                        // Reduce the vector to scalar
                        Avx2.Store(avxBuffer, vhash);
                        for (int i = 0; i < 8; i++)
                        {
                            hash = (hash * 33) ^ avxBuffer[i];
                        }
                    }

                    // SSE2 optimized path for remaining 16-byte chunks
                    if (Sse2.IsSupported)
                    {
                        Vector128<int> v33_4 = Vector128.Create(33 * 33 * 33 * 33);

                        while (data + 16 <= end)
                        {
                            Vector128<int> vector = Sse2.LoadVector128(data).AsInt32();
                            Vector128<int> vhash = Vector128.Create(hash);

                            vhash = Sse2.MultiplyLow(vhash.AsInt16(), v33_4.AsInt16()).AsInt32();
                            vhash = Sse2.Xor(vhash, vector);

                            Sse2.Store(sseBuffer, vhash);
                            hash = sseBuffer[0] ^ sseBuffer[1] ^ sseBuffer[2] ^ sseBuffer[3];
                            data += 16;
                        }
                    }

                    // Process remaining 8-byte chunks
                    while (data + 8 <= end)
                    {
                        ulong chunk = Unsafe.ReadUnaligned<ulong>(data);
                        hash = (hash * 33 * 33 * 33 * 33 * 33 * 33 * 33 * 33)
                               ^ (int)chunk
                               ^ (int)(chunk >> 32);
                        data += 8;
                    }

                    // Process remaining 4 bytes
                    if (data + 4 <= end)
                    {
                        uint chunk = Unsafe.ReadUnaligned<uint>(data);
                        hash = (hash * 33 * 33 * 33 * 33) ^ (int)chunk;
                        data += 4;
                    }

                    // Process remaining bytes
                    while (data < end)
                    {
                        hash = (hash * 33) ^ *data++;
                    }

                    return hash;
                }
            }
        }
        #endregion

        #region FNV Hash Function
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe ulong OptimizedFNV64ComputeHash(ReadOnlySpan<byte> source, int start, int length)
        {
            // Parameter validation (unsigned checks for better branch prediction)
            if ((uint)start >= (uint)source.Length || (uint)length > (uint)(source.Length - start))
                throw new ArgumentOutOfRangeException();

            const ulong prime = Fnv64Prime;
            ulong hash = Fnv64OffsetBasis;

            fixed (byte* ptr = &MemoryMarshal.GetReference(source))
            {
                byte* current = ptr + start;
                byte* end = current + length;

                // Process 16 bytes at a time (2 x 8 bytes)
                while (current + 16 <= end)
                {
                    // First 8 bytes
                    hash = (hash ^ *(ulong*)current) * prime;
                    current += 8;

                    // Second 8 bytes
                    hash = (hash ^ *(ulong*)current) * prime;
                    current += 8;
                }

                // Process remaining 8 bytes if available
                if (current + 8 <= end)
                {
                    hash = (hash ^ *(ulong*)current) * prime;
                    current += 8;
                }

                // Process remaining 4 bytes if available
                if (current + 4 <= end)
                {
                    hash = (hash ^ *(uint*)current) * prime;
                    current += 4;
                }

                // Process remaining bytes one by one
                while (current < end)
                {
                    hash = (hash ^ *current++) * prime;
                }
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe ulong UltraFastFNV64ComputeHashAvx2(ReadOnlySpan<byte> source)
        {
            const ulong fnvOffset = 14695981039346656037;
            const ulong fnvPrime = 1099511628211;

            if (!Avx2.IsSupported || source.Length < 128)
                return OptimizedFNV64ComputeHash(source, 0, source.Length);

            ulong hash = fnvOffset;

            fixed (byte* ptr = source)
            {
                byte* current = ptr;
                byte* end = ptr + source.Length;

                // Process 256 bits (32 bytes) at a time
                Vector256<ulong> hashVec = Vector256.Create(hash);
                Vector256<ulong> primeVec = Vector256.Create(fnvPrime);

                while ((end - current) >= 128)
                {
                    var v0 = Avx.LoadVector256((ulong*)(current));
                    var v1 = Avx.LoadVector256((ulong*)(current + 32));
                    var v2 = Avx.LoadVector256((ulong*)(current + 64));
                    var v3 = Avx.LoadVector256((ulong*)(current + 96));

                    // Combine using XOR tree
                    var combined = Avx2.Xor(Avx2.Xor(v0, v1), Avx2.Xor(v2, v3));

                    // Horizontal XOR to reduce to scalar
                    ulong temp = combined.GetElement(0) ^ combined.GetElement(1) ^
                                 combined.GetElement(2) ^ combined.GetElement(3);

                    hash ^= temp;
                    hash *= fnvPrime;

                    current += 128;
                }

                // Fallback: 64-byte blocks (manually)
                while ((end - current) >= 64)
                {
                    ulong lane0 = Unsafe.ReadUnaligned<ulong>(current);
                    ulong lane1 = Unsafe.ReadUnaligned<ulong>(current + 8);
                    ulong lane2 = Unsafe.ReadUnaligned<ulong>(current + 16);
                    ulong lane3 = Unsafe.ReadUnaligned<ulong>(current + 24);
                    ulong lane4 = Unsafe.ReadUnaligned<ulong>(current + 32);
                    ulong lane5 = Unsafe.ReadUnaligned<ulong>(current + 40);
                    ulong lane6 = Unsafe.ReadUnaligned<ulong>(current + 48);
                    ulong lane7 = Unsafe.ReadUnaligned<ulong>(current + 56);

                    ulong combined = lane0 ^ lane1 ^ lane2 ^ lane3 ^ lane4 ^ lane5 ^ lane6 ^ lane7;
                    hash ^= combined;
                    hash *= fnvPrime;

                    current += 64;
                }

                // Remaining 8-byte chunks
                while ((end - current) >= 8)
                {
                    hash ^= *(ulong*)current;
                    hash *= fnvPrime;
                    current += 8;
                }

                // 4-byte
                if ((end - current) >= 4)
                {
                    hash ^= *(uint*)current;
                    hash *= fnvPrime;
                    current += 4;
                }

                // Remaining bytes
                while (current < end)
                {
                    hash ^= *current++;
                    hash *= fnvPrime;
                }
            }

            return hash;
        }
        #endregion
    }
}
