using Windows.Win32;
using System.Runtime.InteropServices;
using Windows.Win32.Devices.BiometricFramework;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;

namespace BioTap.Service;

public sealed class WinBioSensor(ILogger<WinBioSensor> log) : IBiometricSensor
{
    const uint FingerprintFactor = 8; // WINBIO_TYPE_FINGERPRINT
    const int UnknownId = unchecked((int)0x80098003);  // WINBIO_E_UNKNOWN_ID: finger not enrolled
    const int BadCapture = unchecked((int)0x80098008); // WINBIO_E_BAD_CAPTURE: poor placement
    const int Canceled = unchecked((int)0x80098004);   // WINBIO_E_CANCELED

    readonly Lock gate = new();
    uint session;
    bool open;

    public bool Open()
    {
        lock (gate)
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
    }

    public int AcquireFocus() => PInvoke.WinBioAcquireFocus().Value;
    public int ReleaseFocus() => PInvoke.WinBioReleaseFocus().Value;

    public IdentifyResult Identify()
    {
        uint s;
        lock (gate)
        {
            if (!open) return IdentifyResult.DeviceError;
            s = session;
        }
        Span<byte> subFactor = stackalloc byte[1];
        var hr = PInvoke.WinBioIdentify(s, out _, out _, subFactor, out _).Value; // blocking: outside the lock
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
        lock (gate)
            if (open) PInvoke.WinBioCancel(session);
    }

    public void Close()
    {
        lock (gate)
        {
            if (!open) return;
            open = false;
            PInvoke.WinBioCloseSession(session);
        }
    }

    static readonly Guid BiometricClass = new("53D29EF7-377C-4D14-864B-EB3A85769359"); // GUID_DEVCLASS_BIOMETRIC

    public bool IsPresent()
    {
        using var devices = PInvoke.SetupDiGetClassDevs(BiometricClass, null, HWND.Null, SETUP_DI_GET_CLASS_DEVS_FLAGS.DIGCF_PRESENT);
        if (devices.IsInvalid) return true; // unknown: do not report a missing sensor on a failed query
        var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
        return PInvoke.SetupDiEnumDeviceInfo(devices, 0, ref info);
    }

    public void Dispose() => Close();
}
