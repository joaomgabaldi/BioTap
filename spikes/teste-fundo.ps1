# Spike descartavel: o sensor funciona SEM janela em foco? Sai sozinho apos 5 minutos.
$log = Join-Path $PSScriptRoot 'teste-fundo.log'
Add-Type -TypeDefinition @"
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

public static class BgTest {
    [DllImport("winbio.dll")] static extern int WinBioOpenSession(uint factor, uint pool, uint flags, IntPtr units, UIntPtr unitCount, IntPtr db, out uint session);
    [DllImport("winbio.dll")] static extern int WinBioIdentify(uint session, out uint unitId, IntPtr identity, out byte subFactor, out uint rejectDetail);
    [DllImport("winbio.dll")] static extern int WinBioAcquireFocus();
    [DllImport("winbio.dll")] static extern int WinBioReleaseFocus();
    [DllImport("winbio.dll")] static extern int WinBioCloseSession(uint session);

    public static void Run(string logPath, bool admin) {
        Action<string> log = s => File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
        log("inicio, admin=" + admin);
        int hr = WinBioAcquireFocus();
        log("WinBioAcquireFocus hr=0x" + hr.ToString("X8"));

        uint session;
        hr = WinBioOpenSession(8, 1, 0, IntPtr.Zero, UIntPtr.Zero, (IntPtr)1, out session);
        log("WinBioOpenSession hr=0x" + hr.ToString("X8"));
        if (hr != 0) return;

        IntPtr identity = Marshal.AllocHGlobal(256);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMinutes < 5) {
            uint unit, reject; byte sub;
            hr = WinBioIdentify(session, out unit, identity, out sub, out reject);
            log("toque hr=0x" + hr.ToString("X8") + " sub=" + sub);
        }
        Marshal.FreeHGlobal(identity);
        WinBioCloseSession(session);
        WinBioReleaseFocus();
        log("fim");
    }
}
"@
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
[BgTest]::Run($log, $admin)
