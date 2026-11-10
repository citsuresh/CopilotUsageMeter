<#
Purpose: Read Windows process token elevation for cross-integrity pipe verification.
Parameters: TargetProcessId - one or more exact process IDs to inspect.
Read-only: does not modify, attach to, or stop target processes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int[]] $TargetProcessId
)

$ErrorActionPreference = 'Stop'
if (-not ('AgentProcessTokenProbe' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class AgentProcessTokenProbe
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass,
        out int information, int informationLength, out int returnLength);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static bool IsElevated(int processId)
    {
        var process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            IntPtr token;
            if (!OpenProcessToken(process, 8, out token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                int elevation, length;
                if (!GetTokenInformation(token, 20, out elevation, sizeof(int), out length))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return elevation != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}
'@
}

foreach ($id in $TargetProcessId) {
    [pscustomobject]@{
        ProcessId = $id
        IsElevated = [AgentProcessTokenProbe]::IsElevated($id)
    }
}