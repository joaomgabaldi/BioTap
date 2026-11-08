using System.Collections.Concurrent;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>
/// Turns "N taps" into the configured action. Never blocks the caller: actions run on the pool. The same action is
/// never run twice at once (a slow command plus an impatient second tap).
/// </summary>
public sealed class TapRouter(Func<AppConfig> config, PresetCatalog presets)
{
    const int MaxReasonLength = 80;
    readonly ConcurrentDictionary<Guid, byte> running = new();

    public event Action<ActionOutcome>? Outcome;

    public void OnTaps(int n)
    {
        var action = config().Actions.FirstOrDefault(a => a.Enabled && a.Taps == n);
        if (action is null)
        {
            Emit(new(OutcomeKind.NothingConfigured, $"Nada configurado para {n} toques", null, null));
            return;
        }
        if (presets.Get(action.Preset) is not { } preset)
        {
            Emit(new(OutcomeKind.Failed, action.Name, "Preset desconhecido", action.Icon));
            return;
        }
        if (!running.TryAdd(action.Id, 0))
        {
            Emit(new(OutcomeKind.Blocked, action.Name, "Ainda em execução", action.Icon));
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                if (preset.CheckReady(action.Params) is { } reason)
                {
                    Emit(new(OutcomeKind.Blocked, action.Name, reason, action.Icon));
                    return;
                }
                Emit(new(OutcomeKind.Ran, action.Name, null, action.Icon)); // feedback as soon as it starts
                await preset.RunAsync(action.Params, CancellationToken.None);
            }
            catch (Exception e)
            {
                Emit(new(OutcomeKind.Failed, action.Name, Short(e.Message), action.Icon));
            }
            finally
            {
                running.TryRemove(action.Id, out _);
            }
        });
    }

    static string Short(string s) => s.Length <= MaxReasonLength ? s : s[..(MaxReasonLength - 1)] + "…";

    void Emit(ActionOutcome o)
    {
        foreach (var handler in Outcome?.GetInvocationList() ?? [])
        {
            try { ((Action<ActionOutcome>)handler)(o); }
            catch (Exception) { } // a broken overlay must not stop routing
        }
    }
}
