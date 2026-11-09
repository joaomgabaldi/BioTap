using System.Text;
using System.Text.Json.Nodes;
using BioTap.Core.Config;

namespace BioTap.Core.Actions;

/// <summary>Reads the clipboard text (App: WinRT on the UI thread, waits at most 2 s). Null when there is none.</summary>
public interface IClipboardText { string? ReadText(); }

/// <summary>Shows the QR code of a text in a small window.</summary>
public interface IQrWindow { void Show(string text); }

/// <summary>qrCode: no params; encodes whatever text is on the clipboard.</summary>
public sealed class QrCodePreset(IClipboardText clipboard, IQrWindow window) : IPreset
{
    const int MaxChars = 2900, MaxBytes = 2953; // QR version 40-L byte capacity

    public string Id => "qrCode";
    public string DisplayName => "QR code do texto copiado";
    public IconRef DefaultIcon => new("glyph", "ED14");

    public string? ValidateParams(JsonObject p) => null;

    public string? CheckReady(JsonObject p) => Problem(clipboard.ReadText());

    static string? Problem(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Não há texto copiado";
        return text.Length > MaxChars || Encoding.UTF8.GetByteCount(text) > MaxBytes ? "O texto copiado passa de 2.900 caracteres" : null;
    }

    public Task RunAsync(JsonObject p, CancellationToken ct)
    {
        var text = clipboard.ReadText(); // read again: the clipboard may have changed since CheckReady
        if (Problem(text) is { } problem) throw new PresetException(problem);
        window.Show(text!);
        return Task.CompletedTask;
    }
}
