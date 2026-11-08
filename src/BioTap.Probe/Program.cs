// BioTap.Probe: prints what BioTap.Service sends. Run as administrator.
// Usage: BioTap.Probe [--timeout <300..3000>]
using System.Security.Principal;
using BioTap.Core;

if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("Rode como administrador (o pipe só aceita admins).");
    return 1;
}

int? timeoutMs = null;
if (args is ["--timeout", var raw])
{
    if (!int.TryParse(raw, out var ms) || ms < TapCounter.MinTimeoutMs || ms > TapCounter.MaxTimeoutMs)
    {
        Console.Error.WriteLine($"--timeout precisa estar entre {TapCounter.MinTimeoutMs} e {TapCounter.MaxTimeoutMs}.");
        return 2;
    }
    timeoutMs = ms;
}
else if (args.Length > 0)
{
    Console.Error.WriteLine("Uso: BioTap.Probe [--timeout <300..3000>]");
    return 2;
}

var expectedExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BioTap", "BioTap.Service.exe");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.IsCancellationRequested)
{
    await using var client = new PipeClient("BioTap", expectedExe);
    try
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        await client.ConnectAsync(connectTimeout.Token);
        Log("conectado ao BioTap.Service");
        if (timeoutMs is { } t)
        {
            await client.SendAsync(new SetTimeoutMessage(t), cts.Token);
            Log($"timeout enviado: {t} ms");
        }
        await foreach (var m in client.ReadAllAsync(cts.Token))
            Log(m switch
            {
                TapsMessage tap => $"taps {tap.N}",
                StatusMessage s => $"status {s.State}",
                _ => m.ToString()!,
            });
        Log("conexão encerrada pelo serviço");
    }
    catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
    catch (Exception e) when (e is ServerVerificationException or IOException or UnauthorizedAccessException or TimeoutException or OperationCanceledException)
    {
        Log($"erro: {e.Message}");
    }
    try { await Task.Delay(2000, cts.Token); } catch (OperationCanceledException) { break; }
}
return 0;

static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {s}");
