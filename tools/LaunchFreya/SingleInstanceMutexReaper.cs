using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LaunchFreya
{
    // Defeat the EnB client's single-instance guard so a 2nd+ client can run.
    //
    // The client's guard is a Win32 NAMED MUTEX: at startup it does
    //   CreateMutexA(NULL, TRUE, "enb_mutex_lock");
    //   if (GetLastError() == ERROR_ALREADY_EXISTS) -> "Earth & Beyond is already running"
    // The mutex OBJECT lives only while some process holds an open handle to it,
    // so the SECOND client trips the guard purely because the FIRST client still
    // holds that handle. Free the object and the next client starts clean.
    //
    // The robust way to free it -- independent of injection timing or whether the
    // client imported CreateMutexA vs CreateMutexW -- is to reach into the
    // already-running client(s) from OUTSIDE and CLOSE their handle to that mutex:
    // enumerate the system handle table (NtQuerySystemInformation), find the
    // handle in each client.exe whose object name ends in "enb_mutex_lock", and
    // close it in the owning process via DuplicateHandle(DUPLICATE_CLOSE_SOURCE).
    // Once no process holds a handle, the named object is destroyed and a fresh
    // client's CreateMutexA succeeds. The client only checks the mutex ONCE at
    // startup, so closing it afterwards has no effect on the running client.
    //
    // This is the same handle-reaping strategy long-standing Windows multi-client
    // tools use; here it is done 64-bit-cleanly in-process (the launcher is x64,
    // the client is x86 -- DuplicateHandle works across that boundary) and gated
    // to a MULTIBOX launch (2nd+ instance) so the genuine first client keeps its
    // normal single-instance guard.
    //
    // Windows-only: on Linux the launcher runs the client under WINE, where the
    // injected IAT-hook bypass (FreyaPosFeed.dll / FreyaMultiboxHook) already
    // arms; the ntdll/kernel32 calls below have no meaning in a native-Linux .NET
    // process, so ReapOtherClients is a no-op off Windows.
    static class SingleInstanceMutexReaper
    {
        // The exact name the client's single-instance guard uses. It is created in
        // the session's BaseNamedObjects namespace (no "Global\" prefix), so the
        // fully-qualified object name is \Sessions\<n>\BaseNamedObjects\<this> --
        // match on the suffix to stay session-agnostic.
        const string MutexName = "enb_mutex_lock";

        static bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // Close the enb_mutex_lock handle held by every currently-running client
        // process (i.e. the OTHER instances -- ours is not started yet), so the
        // client we are about to launch does not trip the "already running" guard.
        // Best-effort: every failure is logged and swallowed, never thrown, so a
        // reaping problem can never break the launch itself. Returns how many
        // handles were closed.
        public static int ReapOtherClients(string clientExeName, Action<string> log)
        {
            log ??= _ => { };
            if (!OnWindows)
                return 0;

            var targetPids = new HashSet<int>(ClientProcessIds(clientExeName));
            if (targetPids.Count == 0)
                return 0;

            int closed = 0;
            try
            {
                closed = ReapWindows(targetPids, log);
            }
            catch (Exception ex)
            {
                log($"Single-instance bypass: handle reap failed ({ex.Message}); " +
                    "if the 2nd client still says \"already running\", try running the launcher as administrator.");
            }
            if (closed > 0)
                log($"Single-instance bypass: closed {closed} '{MutexName}' handle(s) in the running client(s); a new client can now start.");
            return closed;
        }

        static IEnumerable<int> ClientProcessIds(string clientExeName)
        {
            string procName = string.IsNullOrEmpty(clientExeName)
                ? "client"
                : System.IO.Path.GetFileNameWithoutExtension(clientExeName);
            Process[] procs;
            try { procs = Process.GetProcessesByName(procName); }
            catch { yield break; }
            foreach (var p in procs)
            {
                int id;
                try { id = p.Id; } catch { p.Dispose(); continue; }
                p.Dispose();
                yield return id;
            }
        }

        static int ReapWindows(HashSet<int> targetPids, Action<string> log)
        {
            var entries = QuerySystemHandles();
            if (entries == null)
                return 0;

            IntPtr self = GetCurrentProcess();
            var procHandles = new Dictionary<int, IntPtr>();
            int closed = 0;
            try
            {
                foreach (var e in entries)
                {
                    if (!targetPids.Contains(e.pid))
                        continue;

                    if (!procHandles.TryGetValue(e.pid, out IntPtr proc))
                    {
                        proc = OpenProcess(PROCESS_DUP_HANDLE, false, e.pid);
                        procHandles[e.pid] = proc;  // cache even if IntPtr.Zero
                    }
                    if (proc == IntPtr.Zero)
                        continue;

                    // Read the object type/name via a temporary duplicate. Query
                    // the TYPE first (safe, never blocks) and only ask for the NAME
                    // on mutants -- NtQueryObject(Name) can hang on some handle
                    // kinds (e.g. a pipe with pending I/O), and a client holds none
                    // of those under the Mutant type.
                    if (!DuplicateHandle(proc, e.handle, self, out IntPtr dup,
                                         0, false, DUPLICATE_SAME_ACCESS) || dup == IntPtr.Zero)
                        continue;

                    bool match = false;
                    try
                    {
                        if (QueryObjectString(dup, ObjectTypeInformation) == "Mutant")
                        {
                            string name = QueryObjectString(dup, ObjectNameInformation);
                            match = name != null &&
                                    name.EndsWith("\\" + MutexName, StringComparison.Ordinal);
                        }
                    }
                    finally { CloseHandle(dup); }

                    if (!match)
                        continue;

                    // Close the handle IN THE CLIENT: duplicating with
                    // DUPLICATE_CLOSE_SOURCE closes the source handle; we then drop
                    // the copy we were handed, releasing the object's last ref.
                    if (DuplicateHandle(proc, e.handle, self, out IntPtr sink,
                                        0, false, DUPLICATE_CLOSE_SOURCE))
                    {
                        if (sink != IntPtr.Zero)
                            CloseHandle(sink);
                        closed++;
                    }
                }
            }
            finally
            {
                foreach (var h in procHandles.Values)
                    if (h != IntPtr.Zero) CloseHandle(h);
            }
            return closed;
        }

        // One system handle-table entry we care about: which process owns it and
        // the handle value within that process.
        readonly struct HandleEntry
        {
            public readonly int pid;
            public readonly IntPtr handle;
            public HandleEntry(int pid, IntPtr handle) { this.pid = pid; this.handle = handle; }
        }

        // Enumerate the whole system handle table via
        // SystemExtendedHandleInformation (the pointer-width-clean variant, so the
        // struct layout is unambiguous in this 64-bit process). Grows the buffer
        // until the call stops returning STATUS_INFO_LENGTH_MISMATCH.
        static List<HandleEntry> QuerySystemHandles()
        {
            int size = 0x10000;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int needed = 0;
                uint status;
                while ((status = NtQuerySystemInformation(
                            SystemExtendedHandleInformation, buf, size, ref needed))
                        == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buf);
                    // needed can lag as handles are created between calls; pad it.
                    size = Math.Max(needed, size * 2);
                    buf = Marshal.AllocHGlobal(size);
                }
                if (status != 0)
                    return null;

                // SYSTEM_HANDLE_INFORMATION_EX (x64):
                //   ULONG_PTR NumberOfHandles;  (8)
                //   ULONG_PTR Reserved;         (8)
                //   SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX Handles[];  (40 each)
                // Entry:
                //   PVOID     Object;              (off 0,  8)
                //   ULONG_PTR UniqueProcessId;     (off 8,  8)
                //   ULONG_PTR HandleValue;         (off 16, 8)
                //   ULONG     GrantedAccess;       (off 24, 4)
                //   USHORT    CreatorBackTraceIndex(off 28, 2)
                //   USHORT    ObjectTypeIndex;     (off 30, 2)
                //   ULONG     HandleAttributes;    (off 32, 4)
                //   ULONG     Reserved;            (off 36, 4)
                long count = Marshal.ReadIntPtr(buf).ToInt64();
                const int HeaderSize = 16, EntrySize = 40;
                long maxByBuffer = (size - HeaderSize) / EntrySize;
                if (count > maxByBuffer) count = maxByBuffer;

                var list = new List<HandleEntry>((int)Math.Min(count, 1 << 20));
                for (long i = 0; i < count; i++)
                {
                    long baseOff = HeaderSize + i * EntrySize;
                    int pid = (int)Marshal.ReadIntPtr(buf, (int)(baseOff + 8)).ToInt64();
                    IntPtr handle = Marshal.ReadIntPtr(buf, (int)(baseOff + 16));
                    list.Add(new HandleEntry(pid, handle));
                }
                return list;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // Read the UNICODE_STRING at the head of an NtQueryObject result (both
        // OBJECT_TYPE_INFORMATION and OBJECT_NAME_INFORMATION begin with one) and
        // return it, or null. On x64 UNICODE_STRING is { USHORT Length; USHORT
        // MaximumLength; (4 pad) PWSTR Buffer }, i.e. Length at +0 and Buffer at +8.
        static string QueryObjectString(IntPtr handle, int infoClass)
        {
            int size = 0x800;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                int needed = 0;
                uint status;
                while ((status = NtQueryObject(handle, infoClass, buf, size, ref needed))
                        == STATUS_INFO_LENGTH_MISMATCH)
                {
                    Marshal.FreeHGlobal(buf);
                    size = needed > 0 ? needed : size * 2;
                    buf = Marshal.AllocHGlobal(size);
                }
                if (status != 0)
                    return null;

                ushort len = (ushort)Marshal.ReadInt16(buf);        // UNICODE_STRING.Length (bytes)
                IntPtr strBuf = Marshal.ReadIntPtr(buf, 8);         // UNICODE_STRING.Buffer
                if (strBuf == IntPtr.Zero || len == 0)
                    return string.Empty;
                return Marshal.PtrToStringUni(strBuf, len / 2);
            }
            catch { return null; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // ---- native ------------------------------------------------------------

        const int SystemExtendedHandleInformation = 0x40;
        const uint STATUS_INFO_LENGTH_MISMATCH = 0xC0000004;
        const uint PROCESS_DUP_HANDLE = 0x0040;
        const uint DUPLICATE_CLOSE_SOURCE = 0x1;
        const uint DUPLICATE_SAME_ACCESS = 0x2;
        const int ObjectNameInformation = 1;
        const int ObjectTypeInformation = 2;

        [DllImport("ntdll.dll")]
        static extern uint NtQuerySystemInformation(int systemInformationClass,
            IntPtr systemInformation, int systemInformationLength, ref int returnLength);

        [DllImport("ntdll.dll")]
        static extern uint NtQueryObject(IntPtr handle, int objectInformationClass,
            IntPtr objectInformation, int objectInformationLength, ref int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle,
            IntPtr targetProcess, out IntPtr targetHandle, uint desiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();
    }
}
