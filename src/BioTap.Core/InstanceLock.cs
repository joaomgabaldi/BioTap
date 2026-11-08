namespace BioTap.Core;

/// <summary>
/// Single-instance lock: an exclusively opened file in the admin-only ProgramData\BioTap folder. A non-admin
/// process cannot create or hold it there, so it cannot squat the lock (named kernel objects can be squatted).
/// </summary>
public sealed class InstanceLock : IDisposable
{
    readonly FileStream file;
    InstanceLock(FileStream file) => this.file = file;

    /// <summary>null when another process holds the lock.</summary>
    public static InstanceLock? TryAcquire(string path)
    {
        try
        {
            return new InstanceLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null; // sharing violation: held by another instance
        }
    }

    public void Dispose() => file.Dispose();
}
