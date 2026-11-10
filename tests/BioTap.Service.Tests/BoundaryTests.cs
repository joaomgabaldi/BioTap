using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace BioTap.Service.Tests;

/// <summary>Spec §2/§7: the SYSTEM service never launches processes; handwritten code has no unsafe.</summary>
public class BoundaryTests
{
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "BioTap.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }

    [Fact]
    public void ServiceAssembly_DoesNotReferenceProcessStart()
    {
        var dll = typeof(SensorWorker).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        var refs = md.MemberReferences.Select(h => md.GetMemberReference(h)).Select(m =>
        {
            var parent = m.Parent.Kind == HandleKind.TypeReference ? md.GetString(md.GetTypeReference((TypeReferenceHandle)m.Parent).Name) : "";
            return (Type: parent, Name: md.GetString(m.Name));
        }).ToList();
        Assert.DoesNotContain(refs, r => r.Type == "Process" && r.Name == "Start");
        var typeRefs = md.TypeReferences.Select(h => md.GetString(md.GetTypeReference(h).Name)).ToList();
        Assert.DoesNotContain("ProcessStartInfo", typeRefs);
    }

    [Fact]
    public void ServiceNativeMethods_HasNoProcessLaunchApis()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "src", "BioTap.Service", "NativeMethods.txt"));
        Assert.DoesNotContain(lines, l => l.TrimStart().StartsWith("CreateProcess", StringComparison.OrdinalIgnoreCase)
            || l.TrimStart().StartsWith("ShellExecute", StringComparison.OrdinalIgnoreCase)
            || l.TrimStart().StartsWith("WinExec", StringComparison.OrdinalIgnoreCase));
    }

    // Spec do launcher §6: nada do que chega ao launcher vira caminho ou argumento do processo elevado
    [Fact]
    public void Launcher_DoesNotReadArgumentsOrEnvironment()
    {
        var code = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BioTap.Launcher", "Program.cs"));
        foreach (var banned in new[] { "args", "GetCommandLineArgs", "CommandLine", "GetEnvironmentVariable", "ExpandEnvironmentVariables", "Arguments" })
            Assert.DoesNotContain(banned, code);
    }

    [Fact]
    public void Launcher_ManifestIsAsInvoker()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BioTap.Launcher", "app.manifest"));
        Assert.Contains("level=\"asInvoker\"", manifest);
        Assert.DoesNotContain("requireAdministrator", manifest);
        Assert.DoesNotContain("highestAvailable", manifest);
    }

    // Spec da tradução §7: texto visível em português só nas classes de idioma (Core/Text). Logs ficam em português.
    [Theory]
    [InlineData("BioTap.Core")]
    [InlineData("BioTap.App")]
    public void UserVisibleText_IsInStringsOnly(string project)
    {
        var root = Path.Combine(RepoRoot(), "src", project);
        var literal = new System.Text.RegularExpressions.Regex(@"""(?:[^""\\]|\\.)*""");
        var portuguese = new System.Text.RegularExpressions.Regex("[ãáàâéêíóôõúçÃÁÀÂÉÊÍÓÔÕÚÇ]");
        var log = new System.Text.RegularExpressions.Regex(@"\.Write\(|Log(Information|Warning|Error|Debug|Critical|Trace)\(");
        var offenders = new List<string>();
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, f);
            if (rel.StartsWith("obj") || rel.StartsWith("bin") || rel.StartsWith("Text" + Path.DirectorySeparatorChar)) continue;
            var lines = File.ReadAllLines(f);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//") || log.IsMatch(line) || line.Contains("// ids gravados no config.json: não traduzir")) continue;
                if (literal.Matches(line).Any(m => portuguese.IsMatch(m.Value))) offenders.Add($"{rel}:{i + 1}");
            }
        }
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    // Spec da tradução §6: o idioma do instalador vem só de HKLM (um processo sem admin não escreve lá)
    [Fact]
    public void InstallerLanguage_ReadsOnlyHklm()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var code = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText).ToList();
        Assert.DoesNotContain(code, c => c.Contains("Registry.CurrentUser") || c.Contains("RegistryHive.CurrentUser"));
        Assert.Contains(code, c => c.Contains("""Registry.LocalMachine.OpenSubKey(@"Software\BioTap")"""));
    }

    [Fact]
    public void HandwrittenCode_HasNoUnsafe()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"\bunsafe\b|\bfixed\s*\(|\[DllImport|\[LibraryImport"))
            .ToList();
        Assert.Empty(offenders);
    }
}
