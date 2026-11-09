using BioTap.Core.Config;

namespace BioTap.Core;

public sealed record PanelItem(Guid Id, int Taps, string Name, IconRef Icon);

/// <summary>What the panel and the tray show (spec §4). Pure, so the WinUI layer stays thin.</summary>
public static class PanelModel
{
    public const string EmptyText = "Nenhuma ação configurada";

    /// <summary>Enabled actions, ordered by tap count (the number the user looks up).</summary>
    public static IReadOnlyList<PanelItem> Items(AppConfig c)
        => [.. c.Actions.Where(a => a.Enabled).OrderBy(a => a.Taps).Select(a => new PanelItem(a.Id, a.Taps, a.Name, a.Icon))];

    /// <summary>The sensor state is only meaningful while connected; otherwise the connection explains everything.</summary>
    public static string TrayTooltip(ConnectionState connection, SensorState? sensor) => connection switch
    {
        ConnectionState.Connecting => "BioTap: conectando ao serviço…",
        ConnectionState.ServiceMissing => "BioTap: o serviço não está rodando",
        ConnectionState.NotTrusted => "BioTap: o serviço não é confiável",
        _ => sensor switch
        {
            SensorState.Ok => "BioTap: pronto",
            SensorState.NoSensor => "BioTap: sensor desconectado",
            SensorState.NoFocus => "BioTap: sem acesso ao sensor",
            SensorState.ReleasedForHello => "BioTap: sensor em uso pelo Windows Hello",
            _ => "BioTap: aguardando o sensor…",
        },
    };

    public static bool IsProblem(ConnectionState connection, SensorState? sensor)
        => connection is ConnectionState.ServiceMissing or ConnectionState.NotTrusted
           || (connection == ConnectionState.Connected && sensor is SensorState.NoSensor or SensorState.NoFocus);
}

/// <summary>Spec §4: one Windows notification when trouble starts, not one per event.</summary>
public sealed class ProblemNotifier
{
    bool inTrouble;

    public bool ShouldNotify(ConnectionState connection, SensorState? sensor)
    {
        var now = PanelModel.IsProblem(connection, sensor);
        var notify = now && !inTrouble;
        inTrouble = now;
        return notify;
    }
}
