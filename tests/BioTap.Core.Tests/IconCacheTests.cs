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
}
