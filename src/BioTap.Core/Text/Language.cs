namespace BioTap.Core.Text;

public enum Language { PtBr, En }

/// <summary>Qual idioma vale (spec da tradução §3). Só "pt-BR" e "en" exatos; qualquer outro valor é ignorado.</summary>
public static class LanguageChoice
{
    public static string Code(Language l) => l == Language.En ? "en" : "pt-BR";

    public static Language? Parse(string? code) => code switch
    {
        "pt-BR" => Language.PtBr,
        "en" => Language.En,
        _ => null,
    };

    /// <param name="fromConfig">Escolha em Preferências (config.json).</param>
    /// <param name="fromInstaller">HKLM\Software\BioTap\Language, gravado pelo instalador.</param>
    /// <param name="windowsUiLanguage">Nome da cultura da interface do Windows (ex.: "pt-BR").</param>
    public static Language Resolve(string? fromConfig, string? fromInstaller, string windowsUiLanguage)
        => Parse(fromConfig) ?? Parse(fromInstaller)
           ?? (windowsUiLanguage == "pt" || windowsUiLanguage.StartsWith("pt-", StringComparison.Ordinal) ? Language.PtBr : Language.En);
}
