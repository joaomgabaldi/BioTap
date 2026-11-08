using Microsoft.Extensions.Logging;

namespace BioTap.Core.Tests;

public sealed class ServiceConfigTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-").FullName;
    readonly TestLogger log = new();

    string PathOf(string json)
    {
        var p = Path.Combine(dir, "config.json");
        File.WriteAllText(p, json);
        return p;
    }

    static bool Secure(string _) => true;

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Default_Is800() => Assert.Equal(800, ServiceConfig.Default.TapTimeoutMs);

    [Fact]
    public void Load_MissingFile_ReturnsDefault()
        => Assert.Equal(ServiceConfig.Default, ServiceConfig.Load(Path.Combine(dir, "nope.json"), Secure, log));

    [Fact]
    public void Load_Insecure_ReturnsDefault()
    {
        var cfg = ServiceConfig.Load(PathOf("""{"tapTimeoutMs":2000}"""), _ => false, log);
        Assert.Equal(800, cfg.TapTimeoutMs);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void Load_InvalidJson_ReturnsDefault()
        => Assert.Equal(800, ServiceConfig.Load(PathOf("{oops"), Secure, log).TapTimeoutMs);

    [Theory]
    [InlineData(100)]
    [InlineData(5000)]
    public void Load_OutOfRange_ReturnsDefault(int ms)
        => Assert.Equal(800, ServiceConfig.Load(PathOf("{\"tapTimeoutMs\":" + ms + "}"), Secure, log).TapTimeoutMs);

    [Fact]
    public void Load_Valid_ReturnsValue()
        => Assert.Equal(1200, ServiceConfig.Load(PathOf("""{"version":1,"tapTimeoutMs":1200,"actions":[]}"""), Secure, log).TapTimeoutMs);
}
