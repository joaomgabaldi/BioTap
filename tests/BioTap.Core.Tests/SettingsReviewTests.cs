using BioTap.Core.Actions;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public sealed class SettingsReviewTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("biotap-s8-").FullName;
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public async Task Save_WhenServiceNotificationFails_ReportsItButKeepsTheSave()
    {
        var path = Path.Combine(dir, "config.json");
        var saver = new SettingsSaver(path, new PresetCatalog([]), _ => true, (_, _) => throw new IOException("pipe"));
        var outcome = await saver.SaveAsync(AppConfig.Default, AppConfig.Default with { TapTimeoutMs = 1300 });
        Assert.Equal(new SaveOutcome(true, "Salvo, mas o serviço não recebeu o novo tempo."), outcome);
        Assert.Equal(1300, ConfigStore.Load(path, _ => true, new PresetCatalog([])).Config.TapTimeoutMs);
    }

    [Fact]
    public void StartupTask_RefusesAnExeOutsideAnAdminOnlyFolder()
    {
        // A logon task with highest privileges for a user-writable exe would be a privilege escalation
        var exe = Path.Combine(dir, "BioTap.exe");
        File.WriteAllText(exe, "x");
        Assert.Throws<InvalidOperationException>(() => new StartupTask("BioTap-test-never").Enable(exe));
    }
}
