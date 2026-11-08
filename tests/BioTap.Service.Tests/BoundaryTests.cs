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
