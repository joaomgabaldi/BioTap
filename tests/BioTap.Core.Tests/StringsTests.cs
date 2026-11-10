using BioTap.Core.Text;

namespace BioTap.Core.Tests;

/// <summary>Spec da tradução §2/§7: os dois idiomas completos; textos com valor incluem o valor.</summary>
public class StringsTests
{
    public static TheoryData<Language> Languages => new() { Language.PtBr, Language.En };

    [Theory]
    [MemberData(nameof(Languages))]
    public void NoEmptyText(Language l)
    {
        var s = Strings.For(l);
        var empty = typeof(Strings).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Where(p => string.IsNullOrWhiteSpace((string?)p.GetValue(s)))
            .Select(p => p.Name).ToList();
        Assert.Empty(empty);
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void ForReturnsItsLanguage(Language l)
        => Assert.Equal(l, Strings.For(l).Language);

    [Fact]
    public void CurrentDefaultsToPtBr()
        => Assert.Equal(Language.PtBr, Strings.Current.Language);

    [Theory]
    [MemberData(nameof(Languages))]
    public void MethodsShowTheirValues(Language l)
    {
        var s = Strings.For(l);
        var missing = new List<string>();
        foreach (var m in typeof(Strings).GetMethods().Where(m => m.ReturnType == typeof(string) && m.DeclaringType == typeof(Strings) && m.GetParameters().Length > 0))
        {
            var args = m.GetParameters().Select((p, i) => p.ParameterType == typeof(int) ? (object)(7001 + i) : $"VALOR{i}").ToArray();
            var text = (string)m.Invoke(s, args)!;
            foreach (var a in args)
                if (!text.Contains(a.ToString()!)) missing.Add($"{m.Name}: {a}");
        }
        Assert.Empty(missing);
    }
}
