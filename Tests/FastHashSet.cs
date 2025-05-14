using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace Tests
{
    public static class FnvHash
    {
        private const ulong Fnv64OffsetBasis = 14695981039346656037;
        private const ulong Fnv64Prime = 1099511628211;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe ulong OptimizedFNV64ComputeHash(ReadOnlySpan<byte> source, int start, int length)
        {
            if ((uint)start >= (uint)source.Length || (uint)length > (uint)(source.Length - start))
                throw new ArgumentOutOfRangeException();

            ulong hash = Fnv64OffsetBasis;

            fixed (byte* ptr = &MemoryMarshal.GetReference(source))
            {
                byte* current = ptr + start;
                byte* end = current + length;

                while (current + 16 <= end)
                {
                    hash = (hash ^ *(ulong*)current) * Fnv64Prime;
                    current += 8;
                    hash = (hash ^ *(ulong*)current) * Fnv64Prime;
                    current += 8;
                }

                if (current + 8 <= end)
                {
                    hash = (hash ^ *(ulong*)current) * Fnv64Prime;
                    current += 8;
                }

                if (current + 4 <= end)
                {
                    hash = (hash ^ *(uint*)current) * Fnv64Prime;
                    current += 4;
                }

                while (current < end)
                {
                    hash = (hash ^ *current++) * Fnv64Prime;
                }
            }

            return hash;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public static unsafe ulong UltraFastFNV64ComputeHashAvx2(ReadOnlySpan<byte> source)
        {
            if (!Avx2.IsSupported || source.Length < 128)
                return OptimizedFNV64ComputeHash(source, 0, source.Length);

            ulong hash = Fnv64OffsetBasis;

            fixed (byte* ptr = source)
            {
                byte* current = ptr;
                byte* end = ptr + source.Length;

                Vector256<ulong> primeVec = Vector256.Create(Fnv64Prime);

                while ((end - current) >= 128)
                {
                    var v0 = Avx.LoadVector256((ulong*)(current));
                    var v1 = Avx.LoadVector256((ulong*)(current + 32));
                    var v2 = Avx.LoadVector256((ulong*)(current + 64));
                    var v3 = Avx.LoadVector256((ulong*)(current + 96));

                    ulong temp = v0.GetElement(0) ^ v0.GetElement(1) ^ v0.GetElement(2) ^ v0.GetElement(3)
                               ^ v1.GetElement(0) ^ v1.GetElement(1) ^ v1.GetElement(2) ^ v1.GetElement(3)
                               ^ v2.GetElement(0) ^ v2.GetElement(1) ^ v2.GetElement(2) ^ v2.GetElement(3)
                               ^ v3.GetElement(0) ^ v3.GetElement(1) ^ v3.GetElement(2) ^ v3.GetElement(3);

                    hash ^= temp;
                    hash *= Fnv64Prime;

                    current += 128;
                }

                while ((end - current) >= 64)
                {
                    ulong combined = 0;
                    for (int i = 0; i < 8; i++)
                    {
                        combined ^= Unsafe.ReadUnaligned<ulong>(current + i * 8);
                    }
                    hash ^= combined;
                    hash *= Fnv64Prime;
                    current += 64;
                }

                while ((end - current) >= 8)
                {
                    hash ^= *(ulong*)current;
                    hash *= Fnv64Prime;
                    current += 8;
                }

                if ((end - current) >= 4)
                {
                    hash ^= *(uint*)current;
                    hash *= Fnv64Prime;
                    current += 4;
                }

                while (current < end)
                {
                    hash ^= *current++;
                    hash *= Fnv64Prime;
                }
            }

            return hash;
        }
    }

    public sealed class SpanHashSet : IDisposable
    {
        private const float LoadFactor = 0.72f;

        private struct Entry
        {
            public byte[] Buffer;
            public int Length;
        }

        private Entry[] _entries;
        private ulong[] _hashes;
        private int _count;
        private int _threshold;

        private readonly ArrayPool<byte> _pool = ArrayPool<byte>.Shared;

        public SpanHashSet(int capacity = 16)
        {
            capacity = Math.Max(16, (int)BitOperations.RoundUpToPowerOf2((uint)capacity));
            _entries = new Entry[capacity];
            _hashes = new ulong[capacity];
            _threshold = (int)(capacity * LoadFactor);
        }

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public bool Add(ReadOnlySpan<byte> key)
        {
            ulong hash = FnvHash.UltraFastFNV64ComputeHashAvx2(key);
            return AddInternal(key, hash);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public bool Contains(ReadOnlySpan<byte> key)
        {
            ulong hash = FnvHash.UltraFastFNV64ComputeHashAvx2(key);
            return Find(key, hash) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private bool AddInternal(ReadOnlySpan<byte> key, ulong hash)
        {
            if (_count >= _threshold)
            {
                Resize();
            }

            int mask = _entries.Length - 1;
            int index = (int)(hash & (ulong)mask);

            while (true)
            {
                if (_hashes[index] == 0)
                {
                    var buffer = _pool.Rent(key.Length);
                    key.CopyTo(buffer);

                    _entries[index].Buffer = buffer;
                    _entries[index].Length = key.Length;
                    _hashes[index] = hash;
                    _count++;
                    return true;
                }
                else if (_hashes[index] == hash &&
                         key.SequenceEqual(_entries[index].Buffer.AsSpan(0, _entries[index].Length)))
                {
                    return false;
                }

                index = (index + 1) & mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private int Find(ReadOnlySpan<byte> key, ulong hash)
        {
            int mask = _entries.Length - 1;
            int index = (int)(hash & (ulong)mask);

            while (true)
            {
                if (_hashes[index] == 0)
                    return -1;

                if (_hashes[index] == hash &&
                    key.SequenceEqual(_entries[index].Buffer.AsSpan(0, _entries[index].Length)))
                    return index;

                index = (index + 1) & mask;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        private void Resize()
        {
            int newSize = _entries.Length * 2;
            var oldEntries = _entries;
            var oldHashes = _hashes;

            _entries = new Entry[newSize];
            _hashes = new ulong[newSize];
            _threshold = (int)(newSize * LoadFactor);
            _count = 0;

            for (int i = 0; i < oldHashes.Length; i++)
            {
                if (oldHashes[i] != 0)
                {
                    var oldEntry = oldEntries[i];
                    AddInternal(oldEntry.Buffer.AsSpan(0, oldEntry.Length), oldHashes[i]);
                    _pool.Return(oldEntry.Buffer);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Dispose()
        {
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_hashes[i] != 0)
                {
                    _pool.Return(_entries[i].Buffer);
                    _entries[i].Buffer = null!;
                    _entries[i].Length = 0;
                }
            }

            _count = 0;
            Array.Clear(_hashes);
        }
    }
}
