using System.IO;
using System.Runtime.InteropServices;

namespace AgHouse.App.Interop;

/// <summary>
/// Finds what a pane's shell is running, so closing a pane can warn before it kills an agent or a build.
/// Reads one Toolhelp snapshot of the process table (no handles opened on other processes).
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
    public static IReadOnlyList<string> RunningUnder(int shellPid)
    {
        var all = Snapshot();
        if (all.Count == 0) return [];
        var shellName = all.FirstOrDefault(p => p.Pid == shellPid).Name;

        var found = new List<string>();
        var queue = new Queue<int>([shellPid]);
        var seen = new HashSet<int> { shellPid };
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            foreach (var child in all.Where(p => p.ParentPid == parent && seen.Add(p.Pid)))
            {
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
}
