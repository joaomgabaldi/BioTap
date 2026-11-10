using BioTap.Core.Config;
using BioTap.Core.Text;

namespace BioTap.Core;

public sealed record PanelItem(Guid Id, int Taps, string Name, IconRef Icon);

/// <summary>What the panel and the tray show (spec §4). Pure, so the WinUI layer stays thin.</summary>
public static class PanelModel
{
    public static string EmptyText => Strings.Current.PanelEmpty;

    /// <summary>Enabled actions, ordered by tap count (the number the user looks up).</summary>
    public static IReadOnlyList<PanelItem> Items(AppConfig c)
        => [.. c.Actions.Where(a => a.Enabled).OrderBy(a => a.Taps).Select(a => new PanelItem(a.Id, a.Taps, a.Name, a.Icon))];

    /// <summary>The sensor state is only meaningful while connected; otherwise the connection explains everything.</summary>
    public static string TrayTooltip(ConnectionState connection, SensorState? sensor) => connection switch
    {
        ConnectionState.Connecting => Strings.Current.StatusConnecting,
        ConnectionState.ServiceMissing => Strings.Current.StatusServiceMissing,
        ConnectionState.NotTrusted => Strings.Current.StatusNotTrusted,
        _ => sensor switch
        {
            SensorState.Ok => Strings.Current.StatusReady,
            SensorState.NoSensor => Strings.Current.StatusNoSensor,
            SensorState.NoFocus => Strings.Current.StatusNoFocus,
            SensorState.ReleasedForHello => Strings.Current.StatusHello,
            _ => Strings.Current.StatusWaiting,
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
