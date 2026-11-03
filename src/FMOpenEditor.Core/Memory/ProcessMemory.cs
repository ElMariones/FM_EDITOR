using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace FMOpenEditor.Core.Memory;

/// <summary>Reads and writes the memory of a running process through the Win32 API.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessMemory : IMemoryAccess, IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessQueryInformation = 0x0400;

    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const uint WritableMask = 0x04 | 0x08 | 0x40 | 0x80; // READWRITE, WRITECOPY, EXECUTE_READWRITE, EXECUTE_WRITECOPY

    private readonly nint _handle;

    public int ProcessId { get; }

    public ProcessMemory(int processId)
    {
        ProcessId = processId;
        _handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryInformation, false, processId);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open process {processId}.");
    }

    public unsafe bool Read(nint address, Span<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            return ReadProcessMemory(_handle, address, (nint)p, buffer.Length, out var read) && read == buffer.Length;
        }
    }

    public unsafe bool Write(nint address, ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            return WriteProcessMemory(_handle, address, (nint)p, data.Length, out var written) && written == data.Length;
        }
    }

    public IEnumerable<MemoryRegion> Regions()
    {
        nint address = 0;
        var size = Marshal.SizeOf<MemoryBasicInformation>();
        while (VirtualQueryEx(_handle, address, out var info, size) == size)
        {
            if (info.State == MemCommit && (info.Protect & (PageGuard | PageNoAccess)) == 0 && info.Protect != 0)
            {
                yield return new MemoryRegion(info.BaseAddress, (long)info.RegionSize, (info.Protect & WritableMask) != 0);
            }
            var next = info.BaseAddress + (nint)info.RegionSize;
            if (next <= address) break;
            address = next;
        }
    }

    public void Dispose() => CloseHandle(_handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public uint PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint address, nint buffer, nint size, out nint read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(nint process, nint address, nint buffer, nint size, out nint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int VirtualQueryEx(nint process, nint address, out MemoryBasicInformation info, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}
