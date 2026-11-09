using System.Runtime.InteropServices;
using BioTap.Core.Actions;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace BioTap.App.Os;

/// <summary>IAudio through Core Audio. Called from pool threads (MTA): COM objects are created per call and released right away. Never logs device names.</summary>
sealed class WindowsAudio : IAudio
{
    const string NoName = "Dispositivo sem nome";

    public const string InterfaceSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    // WinRT enumeration: no raw pointers needed; ids are converted to MMDevice ids
    public IReadOnlyList<AudioDevice> Outputs()
    {
        try
        {
            var infos = DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector()).AsTask().GetAwaiter().GetResult();
            var list = new List<AudioDevice>();
            foreach (var info in infos)
                if (AudioIds.FromWinRt(info.Id) is { } id) list.Add(new AudioDevice(id, string.IsNullOrWhiteSpace(info.Name) ? NoName : info.Name));
            return list;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException) { return []; }
    }

    public string? ActiveOutputId()
    {
        try
        {
            var id = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            return string.IsNullOrEmpty(id) ? null : AudioIds.FromWinRt(id);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException) { return null; }
    }

    /// <summary>Name of a saved device that is not connected now; null when Windows no longer knows it.</summary>
    public static string? KnownName(string mmId)
    {
        try { return DeviceInformation.CreateFromIdAsync(@"\\?\SWD#MMDEVAPI#" + mmId + InterfaceSuffix).AsTask().GetAwaiter().GetResult().Name; }
        catch (Exception e) when (e is COMException or InvalidOperationException or ArgumentException) { return null; }
    }

    public void SetDefaultOutput(string id)
    {
        // a failure after the first role can leave the three roles on different devices (same message)
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
        catch (Exception e) when (e is COMException or InvalidCastException) { throw new PresetException("Não foi possível mudar o volume"); }
        finally { Marshal.FinalReleaseComObject(en); }
    }
}
