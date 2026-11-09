using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public class IconCacheTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "biotap-icons-" + Guid.NewGuid().ToString("N"));
    static readonly IconRef Fallback = new("glyph", "E8E5");

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }

    [Theory]
    [InlineData(@"..\x")]
    [InlineData(@"C:\x.png")]
    [InlineData("0123456789abcdef0123456789abcde")]     // 31
    [InlineData("0123456789abcdef0123456789abcdef0")]   // 33
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("")]
    public void PathFor_RejectsAnythingButAGuid(string value) => Assert.Null(IconCache.PathFor(folder, value));

    [Fact]
    public void PathFor_ValidValue_IsInsideIconsFolder()
    {
        var id = Guid.NewGuid().ToString("N").ToUpperInvariant();
        Assert.Equal(Path.Combine(folder, "icons", id + ".png"), IconCache.PathFor(folder, id));
    }

    [Fact]
    public void Store_WritesAndReturnsExeRef()
    {
        var id = Guid.NewGuid();
        var r = IconCache.Store(folder, id, [1, 2, 3], Fallback);
        Assert.Equal(new IconRef("exe", id.ToString("N")), r);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(IconCache.PathFor(folder, r.Value)!));
    }

    [Fact]
    public void Store_NullPng_ReturnsFallback_AndWritesNothing()
    {
        Assert.Equal(Fallback, IconCache.Store(folder, Guid.NewGuid(), null, Fallback));
        Assert.False(Directory.Exists(Path.Combine(folder, "icons")));
    }

    [Fact]
    public void Store_CreatesIconsFolder()
    {
        IconCache.Store(folder, Guid.NewGuid(), [1], Fallback);
        Assert.True(Directory.Exists(Path.Combine(folder, "icons")));
    }

    [Fact]
    public void Remove_DeletesTheFile_AndIgnoresMissing()
    {
        var id = Guid.NewGuid();
        IconCache.Remove(folder, id); // nothing there
        var r = IconCache.Store(folder, id, [1], Fallback);
        IconCache.Remove(folder, id);
        Assert.False(File.Exists(IconCache.PathFor(folder, r.Value)!));
    }

    [Theory]
    [InlineData(@"C:\x\a.exe", true)]
    [InlineData(@"C:\x\a.DLL", true)]
    [InlineData(@"d:\x\a.ico", true)]
    [InlineData(@"C:\x\a.txt", false)]
    [InlineData(@"C:\x\a.lnk", false)]
    [InlineData(@"C:\x\a.bat", false)]
    [InlineData(@"C:\x\folder", false)]
    [InlineData(@"\host\share\a.exe", false)]
    [InlineData(@"\?\C:\x\a.exe", false)]
    [InlineData(@"a.exe", false)]
    [InlineData(@"\x\a.exe", false)]
    public void CanExtractFrom_OnlyLocalDriveExeDllIco(string path, bool expected) => Assert.Equal(expected, IconCache.CanExtractFrom(path));

    [Fact]
    public void Store_OverwritesExisting_AndLeavesNoTmp()
    {
        var id = Guid.NewGuid();
        IconCache.Store(folder, id, [1], Fallback);
        var r = IconCache.Store(folder, id, [9, 9], Fallback);
        Assert.Equal(new byte[] { 9, 9 }, File.ReadAllBytes(IconCache.PathFor(folder, r.Value)!));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "icons"), "*.tmp"));
    }

    [Fact]
    public void Store_WriteFails_ReturnsFallback_AndLeavesNoTmp()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(IconCache.PathFor(folder, id.ToString("N"))!); // a directory where the file should go
        Assert.Equal(Fallback, IconCache.Store(folder, id, [1], Fallback));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "icons"), "*.tmp"));
    }
}
