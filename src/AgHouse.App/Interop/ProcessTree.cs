using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AgHouse.App.Interop;

/// <summary>
/// Finds what a pane's shell is running, so closing a pane can warn before it kills an agent or a build.
/// Reads one Toolhelp snapshot of the process table (no handles opened on other processes, except
/// by <see cref="KillTree"/> on the tree it ends).
/// </summary>
internal static partial class ProcessTree
{
    // Console plumbing that sits under a shell without being "something the user is running".
    private static readonly HashSet<string> Plumbing = new(StringComparer.OrdinalIgnoreCase)
    {
        "conhost.exe", "OpenConsole.exe", "wslhost.exe", "wslrelay.exe",
    };

    /// <summary>
    /// Image names of the programs running under <paramref name="shellPid"/>, nearest first.
    /// A child with the shell's own image name (Git for Windows' bin\bash.exe → usr\bin\bash.exe)
    /// is treated as part of the shell and looked through. Empty when the shell is idle or on error.
    /// </summary>
    public static IReadOnlyList<string> RunningUnder(int shellPid) =>
        RunningUnder([shellPid]).GetValueOrDefault(shellPid) ?? [];

    /// <summary>
    /// <see cref="RunningUnder(int)"/> for several shells from one snapshot of the process table. A
    /// snapshot costs ~9 ms with 450 processes, so asking about every pane before a quit or a
    /// workspace close takes one, not one per pane.
    /// </summary>
    public static Dictionary<int, IReadOnlyList<string>> RunningUnder(IEnumerable<int> shellPids)
    {
        var result = new Dictionary<int, IReadOnlyList<string>>();
        var all = Snapshot();
        if (all.Count == 0) return result;
        var byPid = new Dictionary<int, Entry>(all.Count);
        foreach (var p in all) byPid.TryAdd(p.Pid, p);
        var children = all.ToLookup(p => p.ParentPid);
        foreach (var shellPid in shellPids.Distinct()) result[shellPid] = Under(shellPid, byPid, children);
        return result;
    }

    private static List<string> Under(int shellPid, Dictionary<int, Entry> byPid, ILookup<int, Entry> children)
    {
        var shellName = byPid.TryGetValue(shellPid, out var shell) ? shell.Name : null;
        var found = new List<string>();
        var queue = new Queue<int>([shellPid]);
        var seen = new HashSet<int> { shellPid };
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in children[parent])
            {
                if (!seen.Add(child.Pid)) continue;
                if (Plumbing.Contains(child.Name)) continue;
                if (shellName is not null && string.Equals(child.Name, shellName, StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue(child.Pid); // the shell's own wrapper: look inside it
                    continue;
                }
                found.Add(child.Name);
            }
        }
        return found;
    }

    /// <summary>
    /// For each root pid, that pid plus every process below it, from one snapshot of the process table.
    /// Used to tell which pane owns a listening port.
    /// </summary>
    public static Dictionary<int, HashSet<int>> Descendants(IEnumerable<int> roots) => Descendants(roots, Snapshot());

    /// <summary>
    /// <see cref="Descendants(IEnumerable{int})"/>, plus what runs under each root (as <see cref="RunningUnder(int)"/>
    /// means it), from one snapshot: the port scan asks both every 3 s. Running is null when the snapshot
    /// failed, so nothing is known to have exited.
    /// </summary>
    public static (Dictionary<int, HashSet<int>> Trees, Dictionary<int, IReadOnlyList<string>>? Running) DescendantsAndRunning(IReadOnlyCollection<int> roots)
    {
        var all = Snapshot();
        if (all.Count == 0) return (Descendants(roots, all), null);
        var byPid = new Dictionary<int, Entry>(all.Count);
        foreach (var p in all) byPid.TryAdd(p.Pid, p);
        var children = all.ToLookup(p => p.ParentPid);
        var running = new Dictionary<int, IReadOnlyList<string>>();
        foreach (var pid in roots.Distinct()) running[pid] = Under(pid, byPid, children);
        return (Descendants(roots, all), running);
    }

    private static Dictionary<int, HashSet<int>> Descendants(IEnumerable<int> roots, List<Entry> all)
    {
        var children = all.ToLookup(p => p.ParentPid, p => p.Pid);
        var result = new Dictionary<int, HashSet<int>>();
        foreach (var root in roots.Distinct())
        {
            var set = new HashSet<int> { root };
            var queue = new Queue<int>([root]);
            while (queue.Count > 0)
            {
                // A pid can be reused by a process whose parent died long ago; the set stops cycles.
                foreach (var child in children[queue.Dequeue()])
                {
                    if (set.Add(child)) queue.Enqueue(child);
                }
            }
            result[root] = set;
        }
        return result;
    }

    /// <summary>
    /// Ends <paramref name="rootPid"/> and every process below it, parents first so nothing new gets
    /// spawned meanwhile. One snapshot of the process table, and handles only on the tree itself: .NET's
    /// Process.Kill(entireProcessTree) re-reads every process on the machine for each node it visits,
    /// which took ~300 ms for an agent's tree of 25. A "child" that started before its parent is a pid
    /// that was reused after the real parent died, so it and everything below it is left alone.
    /// </summary>
    public static void KillTree(int rootPid)
    {
        var children = Snapshot().ToLookup(p => p.ParentPid, p => p.Pid);
        using var root = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)rootPid);
        if (root.IsInvalid || !TryCreationTime(root, out var rootCreated)) return;
        TerminateProcess(root, 1);

        var queue = new Queue<(int Pid, long Created)>([(rootPid, rootCreated)]);
        var seen = new HashSet<int> { rootPid };
        while (queue.Count > 0)
        {
            var (parent, parentCreated) = queue.Dequeue();
            foreach (var pid in children[parent])
            {
                if (!seen.Add(pid)) continue;
                using var child = OpenProcess(PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                if (child.IsInvalid || !TryCreationTime(child, out var created) || created < parentCreated) continue;
                TerminateProcess(child, 1);
                queue.Enqueue((pid, created));
            }
        }
    }

    private static bool TryCreationTime(SafeProcessHandle process, out long created) =>
        GetProcessTimes(process, out created, out _, out _, out _);

    private readonly record struct Entry(int Pid, int ParentPid, string Name);

    private static List<Entry> Snapshot()
    {
        var list = new List<Entry>();
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == InvalidHandle) return list;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return list;
            do
            {
                list.Add(new Entry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, Path.GetFileName(entry.szExeFile)));
            }
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }
        return list;
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint PROCESS_TERMINATE = 0x0001;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeProcessHandle hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(SafeProcessHandle hProcess, out long creation, out long exit, out long kernel, out long user);
}
