using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace BetterTaskManager
{
    internal sealed class NativeConnection
    {
        public string Protocol { get; set; } = "";
        public string LocalAddress { get; set; } = "";
        public int LocalPort { get; set; }
        public string RemoteAddress { get; set; } = "";
        public int RemotePort { get; set; }
        public string State { get; set; } = "";
        public int OwningPid { get; set; }
    }

    internal static class NativeNetworkCollector
    {
        private const int AddressFamilyInet = 2;
        private const int AddressFamilyInet6 = 23;
        private const int TcpTableOwnerPidAll = 5;
        private const int UdpTableOwnerPid = 1;
        private const uint NoError = 0;
        private const uint ErrorInsufficientBuffer = 122;
        private const int TableHeaderSize = 4;
        private const int Tcp4RowSize = 24;
        private const int Tcp6RowSize = 56;
        private const int Udp4RowSize = 12;
        private const int Udp6RowSize = 28;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetExtendedTcpTable(
            IntPtr tcpTable,
            ref int tableSize,
            [MarshalAs(UnmanagedType.Bool)] bool order,
            int addressFamily,
            int tableClass,
            uint reserved);

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetExtendedUdpTable(
            IntPtr udpTable,
            ref int tableSize,
            [MarshalAs(UnmanagedType.Bool)] bool order,
            int addressFamily,
            int tableClass,
            uint reserved);

        public static List<NativeConnection> GetAll()
        {
            var rows = new List<NativeConnection>();
            ParseTcpTable(ReadTable(true, AddressFamilyInet), false, rows);
            ParseTcpTable(ReadTable(true, AddressFamilyInet6), true, rows);
            ParseUdpTable(ReadTable(false, AddressFamilyInet), false, rows);
            ParseUdpTable(ReadTable(false, AddressFamilyInet6), true, rows);
            return rows;
        }

        private static byte[] ReadTable(bool tcp, int addressFamily)
        {
            int size = 0;
            uint result = ReadNativeTable(tcp, IntPtr.Zero, ref size, addressFamily);
            if (result != ErrorInsufficientBuffer && result != NoError)
            {
                throw new InvalidOperationException(ApiName(tcp) + " failed while sizing the table with Windows error " + result + ".");
            }

            if (size == 0) return new byte[TableHeaderSize];

            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int returnedSize = size;
                    result = ReadNativeTable(tcp, buffer, ref returnedSize, addressFamily);
                    if (result == NoError)
                    {
                        if (returnedSize < TableHeaderSize || returnedSize > size)
                        {
                            throw new InvalidDataException(ApiName(tcp) + " returned an invalid table size of " + returnedSize + " bytes.");
                        }

                        var data = new byte[returnedSize];
                        Marshal.Copy(buffer, data, 0, returnedSize);
                        return data;
                    }

                    if (result != ErrorInsufficientBuffer)
                    {
                        throw new InvalidOperationException(ApiName(tcp) + " failed with Windows error " + result + ".");
                    }

                    if (returnedSize <= size)
                    {
                        throw new InvalidDataException(ApiName(tcp) + " requested an invalid retry buffer size of " + returnedSize + " bytes.");
                    }

                    size = returnedSize;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            throw new InvalidOperationException(ApiName(tcp) + " table changed too quickly to capture after three attempts.");
        }

        private static uint ReadNativeTable(bool tcp, IntPtr buffer, ref int size, int addressFamily)
        {
            return tcp
                ? GetExtendedTcpTable(buffer, ref size, true, addressFamily, TcpTableOwnerPidAll, 0)
                : GetExtendedUdpTable(buffer, ref size, true, addressFamily, UdpTableOwnerPid, 0);
        }

        private static string ApiName(bool tcp)
        {
            return tcp ? "GetExtendedTcpTable" : "GetExtendedUdpTable";
        }

        private static void ParseTcpTable(byte[] data, bool ipv6, List<NativeConnection> rows)
        {
            int rowSize = ipv6 ? Tcp6RowSize : Tcp4RowSize;
            int count = ValidateTable(data, rowSize, ipv6 ? "IPv6 TCP" : "IPv4 TCP");

            for (int index = 0; index < count; index++)
            {
                int offset = TableHeaderSize + (index * rowSize);
                rows.Add(ipv6 ? ParseTcp6Row(data, offset) : ParseTcp4Row(data, offset));
            }
        }

        private static void ParseUdpTable(byte[] data, bool ipv6, List<NativeConnection> rows)
        {
            int rowSize = ipv6 ? Udp6RowSize : Udp4RowSize;
            int count = ValidateTable(data, rowSize, ipv6 ? "IPv6 UDP" : "IPv4 UDP");

            for (int index = 0; index < count; index++)
            {
                int offset = TableHeaderSize + (index * rowSize);
                rows.Add(ipv6 ? ParseUdp6Row(data, offset) : ParseUdp4Row(data, offset));
            }
        }

        private static int ValidateTable(byte[] data, int rowSize, string tableName)
        {
            if (data == null || data.Length < TableHeaderSize)
            {
                throw new InvalidDataException(tableName + " table is missing its entry-count header.");
            }

            uint rawCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0, TableHeaderSize));
            int maximumRows = (data.Length - TableHeaderSize) / rowSize;
            if (rawCount > maximumRows)
            {
                throw new InvalidDataException(tableName + " table declares " + rawCount + " rows but contains space for only " + maximumRows + ".");
            }

            return checked((int)rawCount);
        }

        private static NativeConnection ParseTcp4Row(byte[] data, int offset)
        {
            return new NativeConnection
            {
                Protocol = "TCP",
                State = TcpState(ReadUInt32(data, offset)),
                LocalAddress = Ipv4Address(data, offset + 4),
                LocalPort = NetworkPort(data, offset + 8),
                RemoteAddress = Ipv4Address(data, offset + 12),
                RemotePort = NetworkPort(data, offset + 16),
                OwningPid = ProcessId(data, offset + 20)
            };
        }

        private static NativeConnection ParseTcp6Row(byte[] data, int offset)
        {
            return new NativeConnection
            {
                Protocol = "TCP",
                LocalAddress = Ipv6Address(data, offset, offset + 16),
                LocalPort = NetworkPort(data, offset + 20),
                RemoteAddress = Ipv6Address(data, offset + 24, offset + 40),
                RemotePort = NetworkPort(data, offset + 44),
                State = TcpState(ReadUInt32(data, offset + 48)),
                OwningPid = ProcessId(data, offset + 52)
            };
        }

        private static NativeConnection ParseUdp4Row(byte[] data, int offset)
        {
            return new NativeConnection
            {
                Protocol = "UDP",
                LocalAddress = Ipv4Address(data, offset),
                LocalPort = NetworkPort(data, offset + 4),
                State = "Listening",
                OwningPid = ProcessId(data, offset + 8)
            };
        }

        private static NativeConnection ParseUdp6Row(byte[] data, int offset)
        {
            return new NativeConnection
            {
                Protocol = "UDP",
                LocalAddress = Ipv6Address(data, offset, offset + 16),
                LocalPort = NetworkPort(data, offset + 20),
                State = "Listening",
                OwningPid = ProcessId(data, offset + 24)
            };
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));
        }

        private static int ProcessId(byte[] data, int offset)
        {
            uint pid = ReadUInt32(data, offset);
            return pid <= int.MaxValue ? (int)pid : 0;
        }

        private static int NetworkPort(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, sizeof(ushort)));
        }

        private static string Ipv4Address(byte[] data, int offset)
        {
            return new IPAddress(data.AsSpan(offset, 4)).ToString();
        }

        private static string Ipv6Address(byte[] data, int addressOffset, int scopeOffset)
        {
            uint scope = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(scopeOffset, sizeof(uint)));
            return new IPAddress(data.AsSpan(addressOffset, 16), scope).ToString();
        }

        private static string TcpState(uint state)
        {
            switch (state)
            {
                case 1: return "Closed";
                case 2: return "Listening";
                case 3: return "Syn Sent";
                case 4: return "Syn Received";
                case 5: return "Established";
                case 6: return "Fin Wait 1";
                case 7: return "Fin Wait 2";
                case 8: return "Close Wait";
                case 9: return "Closing";
                case 10: return "Last Ack";
                case 11: return "Time Wait";
                case 12: return "Delete TCB";
                default: return "State " + state;
            }
        }
    }
}
