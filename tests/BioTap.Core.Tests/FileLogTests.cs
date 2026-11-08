namespace BioTap.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-log-").FullName;
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Write_AppendsLineWithLevel()
    {
        var log = new FileLog(dir, "app", maxBytes: 1000, keep: 5);
        log.Write("ERROR", "boom");
        var text = File.ReadAllText(Path.Combine(dir, "app.log"));
        Assert.Contains("ERROR boom", text);
    }

    [Fact]
    public void Rotates_KeepsAtMostNFiles()
    {
        var log = new FileLog(dir, "app", maxBytes: 200, keep: 5);
        for (var i = 0; i < 200; i++) log.Write("INFO", new string('x', 50));
        var files = Directory.GetFiles(dir).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["app.1.log", "app.2.log", "app.3.log", "app.4.log", "app.log"], files);
        Assert.All(Directory.GetFiles(dir), f => Assert.True(new FileInfo(f).Length <= 200 + 100));
    }

    [Fact]
    public void Write_NeutralizesNewlines() // A1: a message cannot forge extra log lines
    {
        var log = new FileLog(dir, "app", maxBytes: 1000, keep: 5);
        log.Write("INFO", "a\nFAKE line\r\nb");
        Assert.Single(File.ReadAllLines(Path.Combine(dir, "app.log")));
    }

    [Fact]
    public void Write_NeverThrows_WhenFolderMissing()
        => new FileLog(Path.Combine(dir, "nope", "deeper"), "app", 1000, 5).Write("INFO", "x");
}
