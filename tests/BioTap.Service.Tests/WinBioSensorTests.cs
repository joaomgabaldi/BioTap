using Microsoft.Extensions.Logging.Abstractions;

namespace BioTap.Service.Tests;

public class WinBioSensorTests
{
    [Fact]
    public void IsPresent_QueriesSetupApiWithoutThrowing()
    {
        // Real SetupAPI call: proves the marshaling works. On this PC the ChipSailing sensor is plugged in.
        using var s = new WinBioSensor(NullLogger<WinBioSensor>.Instance);
        var present = s.IsPresent();
        Assert.True(present || Environment.GetEnvironmentVariable("CI") is not null);
    }
}
