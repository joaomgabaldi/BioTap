using BioTap.Core.Actions;

namespace BioTap.App.Overlay;

static class Glyphs
{
    public const string Warning = "";
    public const string Error = "";
    public const string Unknown = "";

    /// <summary>Segoe Fluent glyph for an outcome: the action's own glyph, or a status glyph.</summary>
    public static string For(ActionOutcome o) => o.Kind switch
    {
        OutcomeKind.NothingConfigured => Unknown,
        OutcomeKind.Failed => Error,
        OutcomeKind.Blocked => Warning,
        _ => o.Icon is { Kind: "glyph" } i && int.TryParse(i.Value, System.Globalization.NumberStyles.HexNumber, null, out var cp) ? char.ConvertFromUtf32(cp) : "",
    };
}
