using Windows.Win32;
using Windows.Win32.Devices.BiometricFramework;

namespace BioTap.Service;

// Task 1 feasibility probe: acquire sensor focus as SYSTEM and log every touch. Replaced by SensorWorker in Task 6.
public sealed class SensorProbeWorker(ILogger<SensorProbeWorker> log) : BackgroundService
{
    const uint FingerprintFactor = 8; // WINBIO_TYPE_FINGERPRINT

    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() =>
    {
        log.LogInformation("Conta: {User}, sessao {Session}", System.Security.Principal.WindowsIdentity.GetCurrent().Name, Environment.ProcessId);
        var hr = PInvoke.WinBioAcquireFocus();
        log.LogInformation("WinBioAcquireFocus (antes da sessao) hr=0x{Hr:X8}", hr.Value);

        // DatabaseId null = default database for the system pool (as in the WinBio samples)
        hr = PInvoke.WinBioOpenSession(FingerprintFactor, WINBIO_POOL.WINBIO_POOL_SYSTEM, 0, default, null, out var session);
        log.LogInformation("WinBioOpenSession hr=0x{Hr:X8}", hr.Value);
        if (hr.Failed) return;

        hr = PInvoke.WinBioAcquireFocus();
        log.LogInformation("WinBioAcquireFocus (depois da sessao) hr=0x{Hr:X8}", hr.Value);

        // WinBioIdentify blocks until a touch; cancel it so the service can stop
        using var cancelReg = ct.Register(() => PInvoke.WinBioCancel(session));
        Span<byte> subFactor = stackalloc byte[1];
        while (!ct.IsCancellationRequested)
        {
            hr = PInvoke.WinBioIdentify(session, out _, out _, subFactor, out _);
            log.LogInformation("toque hr=0x{Hr:X8}", hr.Value);
        }
        PInvoke.WinBioCloseSession(session);
        PInvoke.WinBioReleaseFocus();
    }, ct);
}
