namespace BioTap.Core.Tests;

public sealed class InstanceLockTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-lock-").FullName;
    string P => Path.Combine(dir, "app.lock");
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void SecondAcquire_FailsWhileFirstHeld()
    {
        using var first = InstanceLock.TryAcquire(P);
        Assert.NotNull(first);
        Assert.Null(InstanceLock.TryAcquire(P));
    }

    [Fact]
    public void Acquire_SucceedsAfterRelease()
    {
        InstanceLock.TryAcquire(P)!.Dispose();
        using var again = InstanceLock.TryAcquire(P);
        Assert.NotNull(again);
    }

    [Fact]
    public void AnotherProcessHoldingTheFile_CountsAsRunning()
    {
        using var other = new FileStream(P, FileMode.OpenOrCreate, FileAccess.Read, FileShare.Read);
        Assert.Null(InstanceLock.TryAcquire(P));
    }
}
