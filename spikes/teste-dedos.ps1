# Teste: mostra qual dedo o sensor reconheceu. Mantenha a janela em foco e toque no sensor.
$log = Join-Path $PSScriptRoot 'teste-dedos.log'
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

public static class FingerTest {
    [DllImport("winbio.dll")] static extern int WinBioOpenSession(uint factor, uint pool, uint flags, IntPtr units, UIntPtr unitCount, IntPtr db, out uint session);
    [DllImport("winbio.dll")] static extern int WinBioIdentify(uint session, out uint unitId, IntPtr identity, out byte subFactor, out uint rejectDetail);
    [DllImport("winbio.dll")] static extern int WinBioCloseSession(uint session);

    static readonly string[] Names = { "desconhecido",
        "polegar direito", "indicador direito", "medio direito", "anelar direito", "minimo direito",
        "polegar esquerdo", "indicador esquerdo", "medio esquerdo", "anelar esquerdo", "minimo esquerdo" };

    public static void Run(string logPath) {
        var form = new Form { Text = "Teste de dedos", Width = 460, Height = 200, TopMost = true };
        var label = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14), TextAlign = ContentAlignment.MiddleCenter,
            Text = "Toque no sensor (mantenha esta janela em foco)" };
        form.Controls.Add(label);
        Action<string> show = s => {
            File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss") + " " + s + Environment.NewLine);
            if (form.IsHandleCreated) form.BeginInvoke((Action)(() => label.Text = s));
        };

        form.Shown += (o, e) => new Thread(() => {
            uint session;
            // 8 = fingerprint, 1 = system pool, (IntPtr)1 = WINBIO_DB_DEFAULT
            int hr = WinBioOpenSession(8, 1, 0, IntPtr.Zero, UIntPtr.Zero, (IntPtr)1, out session);
            if (hr != 0) { show("Falha ao abrir sessao: 0x" + hr.ToString("X8")); return; }
            IntPtr identity = Marshal.AllocHGlobal(256);  // WINBIO_IDENTITY (76 bytes), com folga
            while (!form.IsDisposed) {
                uint unit, reject; byte sub;
                hr = WinBioIdentify(session, out unit, identity, out sub, out reject);
                if (hr == 0) {
                    string name = sub < Names.Length ? Names[sub] : "codigo " + sub;
                    show("Dedo: " + name + "  (subfactor " + sub + ")");
                } else {
                    show("Nao reconhecido: 0x" + hr.ToString("X8") + " reject=" + reject);
                }
            }
            Marshal.FreeHGlobal(identity);
            WinBioCloseSession(session);
        }) { IsBackground = true }.Start();

        Application.Run(form);
    }
}
"@
[FingerTest]::Run($log)
