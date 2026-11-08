using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Tests;

public class PanelModelTests
{
    static ActionConfig A(int taps, bool enabled = true)
        => new(Guid.NewGuid(), taps, $"a{taps}", enabled, new IconRef("glyph", "E70B"), "openTarget", new JsonObject());

    [Fact]
    public void Items_SortedByTaps_DisabledHidden()
    {
        var items = PanelModel.Items(AppConfig.Default with { Actions = [A(5), A(1), A(3, enabled: false), A(2)] });
        Assert.Equal([1, 2, 5], items.Select(i => i.Taps));
    }

    [Fact]
    public void Empty_HasMessage()
    {
        Assert.Empty(PanelModel.Items(AppConfig.Default));
        Assert.Equal("Nenhuma ação configurada", PanelModel.EmptyText);
    }

    [Theory]
    [InlineData(ConnectionState.Connected, SensorState.Ok, "BioTap: pronto")]
    [InlineData(ConnectionState.Connected, SensorState.NoSensor, "BioTap: sensor desconectado")]
    [InlineData(ConnectionState.Connected, SensorState.NoFocus, "BioTap: sem acesso ao sensor")]
    [InlineData(ConnectionState.Connected, SensorState.ReleasedForHello, "BioTap: sensor em uso pelo Windows Hello")]
    [InlineData(ConnectionState.Connecting, SensorState.Ok, "BioTap: conectando ao serviço…")]
    [InlineData(ConnectionState.ServiceMissing, SensorState.Ok, "BioTap: o serviço não está rodando")]
    [InlineData(ConnectionState.NotTrusted, SensorState.Ok, "BioTap: o serviço não é confiável")]
    public void TrayTooltip_PerState(ConnectionState c, SensorState s, string expected)
        => Assert.Equal(expected, PanelModel.TrayTooltip(c, s));

    [Fact]
    public void TrayTooltip_ConnectedWithoutStatusYet()
        => Assert.Equal("BioTap: aguardando o sensor…", PanelModel.TrayTooltip(ConnectionState.Connected, null));

    [Fact]
    public void ProblemNotifier_NotifiesOncePerTransitionIntoAProblem()
    {
        var n = new ProblemNotifier();
        Assert.False(n.ShouldNotify(ConnectionState.Connected, SensorState.Ok));
        Assert.True(n.ShouldNotify(ConnectionState.Connected, SensorState.NoSensor));
        Assert.False(n.ShouldNotify(ConnectionState.Connected, SensorState.NoSensor)); // still the same problem
        Assert.False(n.ShouldNotify(ConnectionState.ServiceMissing, null));            // still in trouble
        Assert.False(n.ShouldNotify(ConnectionState.Connected, SensorState.Ok));
        Assert.True(n.ShouldNotify(ConnectionState.NotTrusted, null));
    }

    [Fact]
    public void ProblemNotifier_HelloAndConnectingAreNotProblems()
    {
        var n = new ProblemNotifier();
        Assert.False(n.ShouldNotify(ConnectionState.Connected, SensorState.ReleasedForHello));
        Assert.False(n.ShouldNotify(ConnectionState.Connecting, null));
    }
}
