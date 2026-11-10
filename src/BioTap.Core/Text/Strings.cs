namespace BioTap.Core.Text;

/// <summary>
/// Todo texto que aparece na tela (spec da tradução §2). Um membro abstrato por texto: um idioma sem tradução não
/// compila. Ler <see cref="Current"/> na hora de mostrar; nunca guardar o texto traduzido.
/// </summary>
public abstract class Strings
{
    static readonly Strings PtBrInstance = new PtBr(), EnInstance = new En();

    public static Strings For(Language l) => l == Language.En ? EnInstance : PtBrInstance;

    /// <summary>Idioma em uso. Os testes não trocam (é estático e eles rodam em paralelo).</summary>
    public static Strings Current { get; set; } = PtBrInstance;

    public abstract Language Language { get; }

    // Preferências → Geral
    public abstract string LanguageLabel { get; }
    public string LanguagePtBr => "Português (Brasil)";
    public string LanguageEn => "English";
}
