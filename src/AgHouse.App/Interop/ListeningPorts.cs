using System.Runtime.InteropServices;

namespace AgHouse.App.Interop;

/// <summary>
/// TCP ports in the LISTEN state and the process that owns each, IPv4 and IPv6 (GetExtendedTcpTable).
/// Shown in the sidebar as a workspace's ports, the way cmux shows a dev server's.
/// </summary>
internal static class ListeningPorts
{
    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;
    private const uint NO_ERROR = 0;
    private const uint ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>(owning pid, local port) for every listening socket. Empty on error.</summary>
    public static List<(int Pid, int Port)> Snapshot()
    {
        var list = new List<(int, int)>();
        Read(AF_INET, rowSize: 24, portOffset: 8, pidOffset: 20, list);    // MIB_TCPROW_OWNER_PID
        Read(AF_INET6, rowSize: 56, portOffset: 20, pidOffset: 52, list);  // MIB_TCP6ROW_OWNER_PID
        return list;
    }

    private static void Read(int family, int rowSize, int portOffset, int pidOffset, List<(int, int)> into)
    {
        var size = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
        if (result != ERROR_INSUFFICIENT_BUFFER || size <= 0) return;

        // The table can grow between the two calls; retry a few times with the size it asks for.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
                if (result == ERROR_INSUFFICIENT_BUFFER) continue;
                if (result != NO_ERROR) return;

                var count = Marshal.ReadInt32(buffer);
                var row = buffer + 4;
                for (var i = 0; i < count; i++, row += rowSize)
                {
                    // dwLocalPort holds the port in network byte order in its low 16 bits.
                    var raw = (uint)Marshal.ReadInt32(row + portOffset);
                    var port = (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));
                    var pid = Marshal.ReadInt32(row + pidOffset);
                    into.Add((pid, port));
                }
                return;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf,
        int tableClass, uint reserved);
}
