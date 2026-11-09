using System.Runtime.InteropServices;

namespace BioTap.App.Os;

enum PolicyRole { Console = 0, Multimedia = 1, Communications = 2 }

/// <summary>Undocumented IPolicyConfig (the only handwritten interop). Slots before SetDefaultEndpoint are placeholders and are never called.</summary>
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    [PreserveSig] int Unused1();
    [PreserveSig] int Unused2();
    [PreserveSig] int Unused3();
    [PreserveSig] int Unused4();
    [PreserveSig] int Unused5();
    [PreserveSig] int Unused6();
    [PreserveSig] int Unused7();
    [PreserveSig] int Unused8();
    [PreserveSig] int Unused9();
    [PreserveSig] int Unused10();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PolicyRole role);
}

[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
class PolicyConfigClient { }
