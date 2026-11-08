using Windows.Win32;
using Windows.Win32.Devices.BiometricFramework;

namespace BioTap.Service;

public sealed class WinBioSensor(ILogger<WinBioSensor> log) : IBiometricSensor
{
    const uint FingerprintFactor = 8; // WINBIO_TYPE_FINGERPRINT
    const int UnknownId = unchecked((int)0x80098003);  // WINBIO_E_UNKNOWN_ID: finger not enrolled
    const int BadCapture = unchecked((int)0x80098008); // WINBIO_E_BAD_CAPTURE: poor placement
    const int Canceled = unchecked((int)0x80098004);   // WINBIO_E_CANCELED

    volatile uint session;
    volatile bool open;

    public bool Open()
    {
        if (open) return true;
        // DatabaseId null = default database of the system pool
        var hr = PInvoke.WinBioOpenSession(FingerprintFactor, WINBIO_POOL.WINBIO_POOL_SYSTEM, 0, default, null, out var s);
        if (hr.Failed)
        {
            log.LogWarning("WinBioOpenSession hr=0x{Hr:X8}", hr.Value);
            return false;
        }
        session = s;
        open = true;
        return true;
    }

    public int AcquireFocus() => PInvoke.WinBioAcquireFocus().Value;
    public int ReleaseFocus() => PInvoke.WinBioReleaseFocus().Value;

    public IdentifyResult Identify()
    {
        Span<byte> subFactor = stackalloc byte[1];
        var hr = PInvoke.WinBioIdentify(session, out _, out _, subFactor, out _).Value;
        switch (hr)
        {
            case 0 or UnknownId or BadCapture: return IdentifyResult.Touch;
            case Canceled: return IdentifyResult.Canceled;
            default:
                log.LogWarning("WinBioIdentify hr=0x{Hr:X8}", hr);
                return IdentifyResult.DeviceError;
        }
    }

    public void Cancel()
    {
        if (open) PInvoke.WinBioCancel(session);
    }

    public void Dispose()
    {
        if (!open) return;
        open = false;
        PInvoke.WinBioCloseSession(session);
    }
}
