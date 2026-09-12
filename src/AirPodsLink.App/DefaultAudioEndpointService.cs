using System.Runtime.InteropServices;

namespace AirPodsLink.App;

internal static class DefaultAudioEndpointService
{
    public static string? TrySetForMedia(string? endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return "No hay identificador de endpoint para seleccionarlo.";
        IPolicyConfig? policy = null;
        try
        {
            policy = (IPolicyConfig)new PolicyConfigClient();
            foreach (var role in new[] { ERole.Console, ERole.Multimedia })
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(endpointId, role));
            return null;
        }
        catch (Exception error)
        {
            return $"No se pudo seleccionar la salida predeterminada: {error.Message}";
        }
        finally
        {
            if (policy is not null && Marshal.IsComObject(policy)) Marshal.ReleaseComObject(policy);
        }
    }

    private enum ERole { Console, Multimedia, Communications }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid FormatId; public int PropertyId; }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public short VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat(string deviceId, bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(string deviceId);
        [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string deviceId, bool defaultPeriod, IntPtr defaultValue, IntPtr minimumValue);
        [PreserveSig] int SetProcessingPeriod(string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode(string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode(string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string deviceId, bool store, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue(string deviceId, bool store, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility(string deviceId, bool visible);
    }
}
