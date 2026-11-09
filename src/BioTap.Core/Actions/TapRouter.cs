using System.Collections.Concurrent;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>A failure whose message is safe to show on screen (no command line, path, typed text or clipboard).</summary>
public sealed class PresetException(string userMessage) : Exception(userMessage);

/// <summary>
/// Turns "N taps" into the configured action. Never blocks or throws to the caller: actions run on the pool,
/// with a timeout. The same action never runs twice at once. Outcomes: Ran when it starts, then Failed if it fails.
/// </summary>
public sealed class TapRouter(Func<AppConfig> config, PresetCatalog presets, TimeProvider? time = null)
{
    static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(60);
    readonly TimeProvider clock = time ?? TimeProvider.System;
    readonly ConcurrentDictionary<Guid, byte> running = new();

    public event Action<ActionOutcome>? Outcome;

    public void OnTaps(int n)
    {
        ActionConfig? action;
        try
        {
            action = config().Actions.FirstOrDefault(a => a.Enabled && a.Taps == n);
        }
        catch (Exception)
        {
            Emit(new(OutcomeKind.Failed, $"{n} toques", "Erro interno", null));
            return;
        }
        if (action is null)
        {
            Emit(new(OutcomeKind.NothingConfigured, $"Nada configurado para {n} toques", null, null));
            return;
        }
        Start(action);
    }

    /// <summary>The preferences "Testar" button: runs this action now, enabled or not, through the same safe path.</summary>
    public void Test(ActionConfig action) => Start(action);

    void Start(ActionConfig action)
    {
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
        _ = Task.Run(() => RunAsync(action, preset));
    }

    async Task RunAsync(ActionConfig action, IPreset preset)
    {
        try
        {
            // Last decision point: "Testar" reaches here with actions the loader disabled as invalid
            if ((preset.ValidateParams(action.Params) ?? preset.CheckReady(action.Params)) is { } reason)
            {
                Emit(new(OutcomeKind.Blocked, action.Name, reason, action.Icon));
                return;
            }
            Emit(new(OutcomeKind.Ran, action.Name, null, action.Icon)); // feedback as soon as it starts
            using var cts = new CancellationTokenSource(ActionTimeout, clock);
            await preset.RunAsync(action.Params, cts.Token).WaitAsync(ActionTimeout, clock);
        }
        catch (TimeoutException)
        {
            Emit(new(OutcomeKind.Failed, action.Name, "Tempo esgotado", action.Icon));
        }
        catch (PresetException e)
        {
            Emit(new(OutcomeKind.Failed, action.Name, e.Message, action.Icon));
        }
        catch (Exception)
        {
            // Raw exception messages can carry command lines, paths or typed text: never put them on screen
            Emit(new(OutcomeKind.Failed, action.Name, "Erro ao executar", action.Icon));
        }
        finally
        {
            running.TryRemove(action.Id, out _);
        }
    }

    void Emit(ActionOutcome o)
    {
        foreach (var handler in Outcome?.GetInvocationList() ?? [])
        {
            try { ((Action<ActionOutcome>)handler)(o); }
            catch (Exception) { } // a broken overlay must not stop routing
        }
    }
}
