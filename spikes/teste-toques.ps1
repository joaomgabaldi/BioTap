# Teste de velocidade: conta qualquer toque no sensor (sem checar digital). Mantenha a janela em foco.
$log = Join-Path $PSScriptRoot 'teste-toques.log'
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

public static class TapTest {
    [DllImport("winbio.dll")] static extern int WinBioOpenSession(uint factor, uint pool, uint flags, IntPtr units, UIntPtr unitCount, IntPtr db, out uint session);
    // WinBioLocateSensor volta na hora sem toque (sensor unico), entao usamos Identify e contamos qualquer resultado
    [DllImport("winbio.dll")] static extern int WinBioIdentify(uint session, out uint unitId, IntPtr identity, out byte subFactor, out uint rejectDetail);
    [DllImport("winbio.dll")] static extern int WinBioCloseSession(uint session);

    const int WindowMs = 1500; // tempo sem toque que fecha a contagem

    public static void Run(string logPath) {
        var form = new Form { Text = "Teste de toques", Width = 460, Height = 200, TopMost = true };
        var label = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14), TextAlign = ContentAlignment.MiddleCenter,
            Text = "Toque varias vezes, o mais rapido que conseguir" };
        form.Controls.Add(label);
        Action<string> show = s => {
            File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine);
            if (form.IsHandleCreated) form.BeginInvoke((Action)(() => label.Text = s));
        };

        int count = 0;
        long last = 0;
        var clock = Stopwatch.StartNew();
        // Fecha a contagem quando passa WindowMs sem toque
        var timer = new System.Threading.Timer(_ => {
            int c = Interlocked.Exchange(ref count, 0);
            if (c > 0) show("ACAO: " + c + " toque(s)");
        });

        form.Shown += (o, e) => new Thread(() => {
            uint session;
            int hr = WinBioOpenSession(8, 1, 0, IntPtr.Zero, UIntPtr.Zero, (IntPtr)1, out session);
            if (hr != 0) { show("Falha ao abrir sessao: 0x" + hr.ToString("X8")); return; }
            IntPtr identity = Marshal.AllocHGlobal(256);
            while (!form.IsDisposed) {
                uint unit, reject; byte sub;
                hr = WinBioIdentify(session, out unit, identity, out sub, out reject);
                long now = clock.ElapsedMilliseconds;
                long gap = last == 0 ? 0 : now - last;
                last = now;
                int c = Interlocked.Increment(ref count);
                timer.Change(WindowMs, Timeout.Infinite);
                show("toque " + c + "  intervalo " + gap + " ms  hr=0x" + hr.ToString("X8"));
            }
            WinBioCloseSession(session);
        }) { IsBackground = true }.Start();

        Application.Run(form);
    }
}
"@
[TapTest]::Run($log)
