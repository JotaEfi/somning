using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.JobObjects;

namespace Somning.Platform.Windows.Processes;

/// <summary>A Windows job that terminates its assigned processes when its handle closes.</summary>
public sealed class JobObject : IDisposable
{
    private readonly SafeFileHandle handle;
    private bool disposed;

    private JobObject(SafeFileHandle handle) => this.handle = handle;

    public static JobObject CreateKillOnClose()
    {
        var handle = PInvoke.CreateJobObject(null, null);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref limits, 1));
            if (!PInvoke.SetInformationJobObject(handle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, bytes))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new JobObject(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!PInvoke.AssignProcessToJobObject(handle, process.SafeHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        handle.Dispose();
    }
}
