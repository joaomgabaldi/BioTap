namespace BioTap.Service;

public enum IdentifyResult { Touch, Canceled, DeviceError }

/// <summary>The WinBio calls the worker needs, behind an interface so the worker can be tested without a sensor.</summary>
public interface IBiometricSensor : IDisposable
{
    /// <summary>HRESULT. Only works after <see cref="Open"/> succeeded.</summary>
    int AcquireFocus();
    int ReleaseFocus();
    /// <summary>Opens the system-pool session; false = no sensor.</summary>
    bool Open();
    /// <summary>Blocks until a touch, a cancel or an error.</summary>
    IdentifyResult Identify();
    /// <summary>Unblocks a pending <see cref="Identify"/> (it returns <see cref="IdentifyResult.Canceled"/>).</summary>
    void Cancel();
    /// <summary>Closes the session (after errors, so <see cref="Open"/> can start a fresh one).</summary>
    void Close();
}
