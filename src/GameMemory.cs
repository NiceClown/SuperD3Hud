using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SuperD3Hud
{
    /// <summary>
    /// Read-only access to another process' memory. Nothing is ever written,
    /// no code is injected and no thread is suspended: the reader only asks for
    /// PROCESS_VM_READ, so the game keeps running at full speed.
    /// </summary>
    public interface IGameMemoryReader
    {
        byte[] Read(IntPtr address, int count);
        /// <summary>
        /// Reads <paramref name="count"/> bytes into the start of a buffer the
        /// caller owns, so hot paths can reuse one buffer instead of allocating.
        /// </summary>
        void Read(IntPtr address, byte[] buffer, int count);
        IntPtr Pointer(IntPtr address);
    }

    /// <summary>
    /// Guarded reader over a live process. Every access is bounds checked and
    /// the number of bytes read per second is capped, so a corrupted address
    /// or a runaway loop can never turn into an endless allocation.
    ///
    /// One instance can be used from several threads: the guard and the
    /// statistics counters are updated with Interlocked operations.
    /// </summary>
    public sealed class GameMemoryReader : IGameMemoryReader, IDisposable
    {
        private const int MaxSingleRead = 0x100000;   // 1 MiB per call
        // A lifetime cap would kill a long-running HUD after a few seconds of
        // 25 ms polling, so the guard is a rate limit. It used to be 256 MiB/s,
        // which normal polling of a dense rift could reach; when it tripped,
        // every read failed for the rest of the second and buffs, health and
        // monsters blinked out. Normal polling now reads a few MiB per second,
        // so this is only a sanity cap against a runaway loop.
        private const long MaxReadPerSecond = 0x40000000;   // 1 GiB per second

        // Process-wide totals over every reader, for diagnostics
        // (bytes per second and calls per second).
        private static long _totalBytesRead;
        private static long _totalReadCalls;

        private readonly SafeProcessHandle _handle;
        private long _bytesRead;
        private long _windowStartTicks = DateTime.UtcNow.Ticks;
        private long _instanceBytes;
        private long _instanceCalls;

        // Scratch for Pointer(), one per thread, so the 8-byte reads that make
        // up most pointer chains do not allocate.
        [ThreadStatic]
        private static byte[] _pointerScratch;

        public GameMemoryReader(int processId)
        {
            // PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ
            _handle = OpenProcess(0x1000 | 0x0010, false, processId);
            if (_handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>Bytes read by every reader of this process since start.</summary>
        public static long TotalBytesRead { get { return Interlocked.Read(ref _totalBytesRead); } }

        /// <summary>ReadProcessMemory calls made by every reader since start.</summary>
        public static long TotalReadCalls { get { return Interlocked.Read(ref _totalReadCalls); } }

        /// <summary>Bytes read through this reader.</summary>
        public long BytesRead { get { return Interlocked.Read(ref _instanceBytes); } }

        /// <summary>ReadProcessMemory calls made through this reader.</summary>
        public long ReadCalls { get { return Interlocked.Read(ref _instanceCalls); } }

        public byte[] Read(IntPtr address, int count)
        {
            if (count <= 0 || count > MaxSingleRead)
                throw new InvalidOperationException("Выход за границы чтения памяти.");
            byte[] buffer = new byte[count];
            Read(address, buffer, count);
            return buffer;
        }

        public void Read(IntPtr address, byte[] buffer, int count)
        {
            if (address.ToInt64() < 0x10000 || count <= 0 || count > MaxSingleRead ||
                buffer == null || count > buffer.Length)
                throw new InvalidOperationException("Выход за границы чтения памяти.");

            Reserve(count);

            UIntPtr read;
            if (!ReadProcessMemory(_handle, address, buffer, (UIntPtr)count, out read) ||
                (long)read != count)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Не удалось прочитать память процесса.");
            }
        }

        public IntPtr Pointer(IntPtr address)
        {
            byte[] scratch = _pointerScratch;
            if (scratch == null)
            {
                scratch = new byte[8];
                _pointerScratch = scratch;
            }
            Read(address, scratch, 8);
            return new IntPtr(BitConverter.ToInt64(scratch, 0));
        }

        /// <summary>
        /// Charges one read against the per-second guard and the statistics.
        /// Lock-free: the window is restarted by whichever thread notices it
        /// expired first, and a racing restart can at worst lose a few bytes
        /// of accounting, which is harmless for a sanity cap.
        /// </summary>
        private void Reserve(int count)
        {
            long now = DateTime.UtcNow.Ticks;
            long start = Interlocked.Read(ref _windowStartTicks);
            if (now - start >= TimeSpan.TicksPerSecond &&
                Interlocked.CompareExchange(ref _windowStartTicks, now, start) == start)
            {
                Interlocked.Exchange(ref _bytesRead, 0);
            }

            long total = Interlocked.Add(ref _bytesRead, count);
            if (total > MaxReadPerSecond)
                throw new InvalidOperationException("Превышен лимит скорости чтения памяти.");

            Interlocked.Add(ref _instanceBytes, count);
            Interlocked.Increment(ref _instanceCalls);
            Interlocked.Add(ref _totalBytesRead, count);
            Interlocked.Increment(ref _totalReadCalls);
        }

        public void Dispose()
        {
            if (_handle != null) _handle.Dispose();
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(
            uint access,
            [MarshalAs(UnmanagedType.Bool)] bool inherit,
            int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(
            SafeProcessHandle process,
            IntPtr address,
            [Out] byte[] buffer,
            UIntPtr size,
            out UIntPtr bytesRead);
    }
}
