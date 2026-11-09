using System.Runtime.InteropServices;
using BioTap.Core.Actions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell.PropertiesSystem;

namespace BioTap.App.Os;

/// <summary>IAudio through Core Audio. Called from pool threads (MTA): COM objects are created per call and released right away. Never logs device names.</summary>
sealed class WindowsAudio : IAudio
{
    const string NoName = "Dispositivo sem nome";

    public IReadOnlyList<AudioDevice> Outputs()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            en.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var coll);
            try
            {
                coll.GetCount(out var count);
                var list = new List<AudioDevice>();
                for (uint i = 0; i < count; i++)
                {
                    coll.Item(i, out var dev);
                    try { list.Add(new AudioDevice(IdOf(dev), NameOf(dev))); }
                    finally { Marshal.FinalReleaseComObject(dev); }
                }
                return list;
            }
            finally { Marshal.FinalReleaseComObject(coll); }
        }
        finally { Marshal.FinalReleaseComObject(en); }
    }

    public string? ActiveOutputId()
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var dev);
            try { return IdOf(dev); }
            finally { Marshal.FinalReleaseComObject(dev); }
        }
        catch (COMException) { return null; } // no default output
        finally { Marshal.FinalReleaseComObject(en); }
    }

    public void SetDefaultOutput(string id)
    {
        IPolicyConfig? cfg = null;
        try
        {
            cfg = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { PolicyRole.Console, PolicyRole.Multimedia, PolicyRole.Communications })
                if (cfg.SetDefaultEndpoint(id, role) < 0) throw new PresetException("Não foi possível trocar a saída");
        }
        catch (COMException) { throw new PresetException("Não foi possível trocar a saída"); }
        catch (InvalidCastException) { throw new PresetException("Não foi possível trocar a saída"); }
        finally { if (cfg is not null) Marshal.FinalReleaseComObject(cfg); }
    }

    public void SetVolume(int percent)
    {
        var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            en.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var dev);
            try
            {
                dev.Activate(typeof(IAudioEndpointVolume).GUID, CLSCTX.CLSCTX_ALL, null, out var o);
                try { ((IAudioEndpointVolume)o).SetMasterVolumeLevelScalar(percent / 100f, Guid.Empty); }
                finally { Marshal.FinalReleaseComObject(o); }
            }
            finally { Marshal.FinalReleaseComObject(dev); }
        }
        catch (COMException) { throw new PresetException("Não foi possível mudar o volume"); }
        finally { Marshal.FinalReleaseComObject(en); }
    }

    static string IdOf(IMMDevice dev)
    {
        dev.GetId(out var id);
        try { return id.ToString(); }
        finally { Marshal.FreeCoTaskMem(System.Runtime.CompilerServices.Unsafe.As<PWSTR, nint>(ref id)); } // PWSTR is one pointer; no safe accessor
    }

    static string NameOf(IMMDevice dev)
    {
        try
        {
            dev.OpenPropertyStore(STGM.STGM_READ, out var store);
            try
            {
                var key = PInvoke.PKEY_Device_FriendlyName;
                store.GetValue(key, out var pv);
                try
                {
                    Span<char> buf = stackalloc char[256];
                    PInvoke.PropVariantToString(pv, buf);
                    var name = buf[..buf.IndexOf('\0')].ToString();
                    return name.Length > 0 ? name : NoName;
                }
                finally { PInvoke.PropVariantClear(ref pv); }
            }
            finally { Marshal.FinalReleaseComObject(store); }
        }
        catch (Exception e) when (e is COMException or ArgumentException) { return NoName; }
    }
}
