using System.Text.Json.Nodes;
using BioTap.Core.Actions;

namespace BioTap.Core.Config;

/// <summary>Edits to the action list for the preferences window (spec §4). Pure: returns new configs.</summary>
public static class ActionEditor
{
    /// <summary>A new action pre-filled with the smallest free tap count and the preset's name and icon.</summary>
    public static ActionConfig NewAction(AppConfig c, IPreset preset)
        => new(Guid.NewGuid(), ConfigValidator.NextFreeTaps(c.Actions.Select(a => a.Taps)), preset.DisplayName, true,
               preset.DefaultIcon, preset.Id, new JsonObject());

    /// <summary>null when the edited action can be saved; otherwise the first problem, ready to show.</summary>
    public static string? Check(AppConfig c, ActionConfig edited, IPresetCatalog presets)
    {
        if (c.Actions.FirstOrDefault(a => a.Id != edited.Id && a.Taps == edited.Taps) is { } other)
            return $"Já usado por {other.Name}";
        var errors = ConfigValidator.Validate(WithAction(c, edited), presets).Where(e => e.ActionId == edited.Id);
        return errors.Select(e => e.Message).FirstOrDefault();
    }

    public static AppConfig WithAction(AppConfig c, ActionConfig a)
        => c with { Actions = c.Actions.Any(x => x.Id == a.Id) ? [.. c.Actions.Select(x => x.Id == a.Id ? a : x)] : [.. c.Actions, a] };

    public static AppConfig Without(AppConfig c, Guid id) => c with { Actions = [.. c.Actions.Where(a => a.Id != id)] };
}

/// <param name="Saved">The file now holds the new config.</param>
/// <param name="Message">null when everything worked; otherwise what to tell the user.</param>
public sealed record SaveOutcome(bool Saved, string? Message);

/// <summary>Saves the config and tells the service when the tap timeout changed (spec §4: changes apply on save).</summary>
public sealed class SettingsSaver(string path, IPresetCatalog presets, Func<string, bool> isSecure, Func<int, CancellationToken, Task> sendTimeout)
{
    /// <summary>Never throws. When not saved, the file on disk is left untouched.</summary>
    public async Task<SaveOutcome> SaveAsync(AppConfig before, AppConfig after)
    {
        try
        {
            ConfigStore.Save(path, after, presets, isSecure);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new(false, e is InvalidOperationException ? e.Message : "Não foi possível gravar a configuração.");
        }
        if (after.TapTimeoutMs == before.TapTimeoutMs) return new(true, null);
        try
        {
            await sendTimeout(after.TapTimeoutMs, CancellationToken.None);
        }
        catch (Exception)
        {
            return new(true, "Salvo, mas o serviço não recebeu o novo tempo."); // it reads the file on its next start
        }
        return new(true, null);
    }
}
