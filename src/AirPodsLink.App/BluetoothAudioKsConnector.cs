using AirPodsLink.Core;
using System.Runtime.InteropServices;

namespace AirPodsLink.App;

/// <summary>
/// Requests an A2DP reconnection through Windows' documented BtAudio KS property.
/// This does not replace or bypass the inbox Bluetooth/audio drivers.
/// </summary>
internal sealed class BluetoothAudioKsConnector
{
    private const uint StgmRead = 0;
    private const uint KsPropertyTypeGet = 0x1;
    private const uint KsPropertyTypeBasicSupport = 0x00000200;
    private const uint OneShotReconnect = 0;

    private static readonly Guid BtAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly PropertyKey FriendlyNameKey = new(
        new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    public KsReconnectResult TryReconnectAirPods()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(
                EDataFlow.Render, AirPodsAudioEndpoints.UsableStates, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));

            // Rank every candidate before touching any of them. Picking the
            // first name match would happily send the reconnect order to a
            // leftover endpoint from an earlier pairing.
            var candidates = new List<(string Name, string Id, int Rank)>();
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? scan = null;
                try
                {
                    Marshal.ThrowExceptionForHR(collection.Item(i, out scan));
                    var scanName = SafeGetFriendlyName(scan);
                    if (!AirPodsAudioEndpoints.IsStereoName(scanName)) continue;
                    Marshal.ThrowExceptionForHR(scan.GetId(out var scanId));
                    Marshal.ThrowExceptionForHR(scan.GetState(out var scanState));
                    var rank = AirPodsAudioEndpoints.Rank(scanState);
                    if (rank > 0) candidates.Add((scanName!, scanId, rank));
                }
                catch
                {
                    // Ghost endpoints throw 0xE000020B while being read; skip them.
                }
                finally
                {
                    Release(scan);
                }
            }

            string? candidateName = null;
            string? candidateId = null;
            Exception? lastCandidateError = null;
            foreach (var candidate in candidates.OrderByDescending(item => item.Rank))
            {
                IMMDevice? endpoint = null;
                try
                {
                    var name = candidate.Name;
                    var id = candidate.Id;
                    Marshal.ThrowExceptionForHR(enumerator.GetDevice(id, out endpoint));
                    candidateName ??= name;
                    candidateId ??= id;

                    if (!TryGetKsControl(endpoint, enumerator, out var ksControl, out var topologyError))
                        continue;

                    try
                    {
                        var property = new KsProperty(BtAudioPropertySet, OneShotReconnect, KsPropertyTypeGet);
                        var hr = ksControl!.KsProperty(
                            ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                        return hr >= 0
                            ? new(true, true, name, id, hr, null)
                            : new(true, false, name, id, hr, $"IKsControl.KsProperty falló (0x{hr:X8}).");
                    }
                    finally
                    {
                        Release(ksControl);
                    }
                }
                catch (Exception error)
                {
                    // A candidate can disappear during the A2DP state
                    // transition. Keep trying the remaining ranked candidates.
                    lastCandidateError = error;
                }
                finally
                {
                    Release(endpoint);
                }
            }

            return candidateId is null
                ? new(false, false, null, null, null, "Windows no expuso un endpoint A2DP AirPods activo o desconectado.")
                : new(true, false, candidateName, candidateId, lastCandidateError?.HResult,
                    lastCandidateError?.Message ?? "El endpoint no expuso KSPROPSETID_BtAudio.");
        }
        catch (Exception error)
        {
            return new(false, false, null, null, error.HResult, $"{error.GetType().Name}: {error.Message}");
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }

    private static bool TryGetKsControl(
        IMMDevice endpoint,
        IMMDeviceEnumerator enumerator,
        out IKsControl? ksControl,
        out string? error)
    {
        ksControl = null;
        error = null;
        IDeviceTopology? topology = null;
        try
        {
            var topologyGuid = typeof(IDeviceTopology).GUID;
            Marshal.ThrowExceptionForHR(endpoint.Activate(ref topologyGuid, 23, IntPtr.Zero, out var topologyObject));
            topology = (IDeviceTopology)topologyObject;
            Marshal.ThrowExceptionForHR(topology.GetConnectorCount(out var count));

            for (uint i = 0; i < count; i++)
            {
                IConnector? connector = null;
                IConnector? connected = null;
                IPart? part = null;
                IDeviceTopology? adapterTopology = null;
                IMMDevice? adapterDevice = null;
                try
                {
                    if (topology.GetConnector(i, out connector) < 0 ||
                        connector.GetConnectedTo(out connected) < 0)
                        continue;

                    part = (IPart)connected;
                    Marshal.ThrowExceptionForHR(part.GetTopologyObject(out adapterTopology));
                    Marshal.ThrowExceptionForHR(adapterTopology.GetDeviceId(out var adapterId));
                    Marshal.ThrowExceptionForHR(enumerator.GetDevice(adapterId, out adapterDevice));

                    var ksGuid = typeof(IKsControl).GUID;
                    var hr = adapterDevice.Activate(ref ksGuid, 23, IntPtr.Zero, out var controlObject);
                    if (hr < 0) continue;
                    ksControl = (IKsControl)controlObject;

                    var probe = new KsProperty(BtAudioPropertySet, OneShotReconnect, KsPropertyTypeBasicSupport);
                    var supportBuffer = Marshal.AllocHGlobal(64);
                    try
                    {
                        hr = ksControl.KsProperty(ref probe, (uint)Marshal.SizeOf<KsProperty>(), supportBuffer, 64, out _);
                        if (hr >= 0) return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(supportBuffer);
                    }
                    Release(ksControl);
                    ksControl = null;
                }
                finally
                {
                    Release(adapterDevice);
                    Release(adapterTopology);
                    Release(part);
                    Release(connected);
                    Release(connector);
                }
            }

            error = "No se encontró el filtro KS del adaptador Bluetooth.";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Release(ksControl);
            ksControl = null;
            return false;
        }
        finally
        {
            Release(topology);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            Marshal.ThrowExceptionForHR(device.OpenPropertyStore(StgmRead, out store));
            var key = FriendlyNameKey;
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try { return value.GetString() ?? string.Empty; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    private static string? SafeGetFriendlyName(IMMDevice? device)
    {
        try { return device is null ? null : GetFriendlyName(device); }
        catch { return null; }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant variant);

    internal sealed record KsReconnectResult(
        bool EndpointFound, bool RequestSent, string? EndpointName, string? EndpointId, int? HResult, string? Error);

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty
    {
        public Guid Set;
        public uint Id;
        public uint Flags;
        public KsProperty(Guid set, uint id, uint flags) { Set = set; Id = id; Flags = flags; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
        public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;
        public string? GetString() => VariantType == 31 && PointerValue != IntPtr.Zero
            ? Marshal.PtrToStringUni(PointerValue) : null;
    }

    private enum EDataFlow { Render, Capture, All }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint classContext, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
        [PreserveSig] int GetSubunitCount(out uint count);
        [PreserveSig] int GetSubunit(uint index, out IntPtr subunit);
        [PreserveSig] int GetPartById(uint id, out IPart part);
        [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, [MarshalAs(UnmanagedType.Bool)] bool rejectMixedPaths, out IntPtr parts);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector connectTo);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector connector);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPart
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetLocalId(out uint id);
        [PreserveSig] int GetGlobalId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetPartType(out int type);
        [PreserveSig] int GetSubType(out Guid type);
        [PreserveSig] int GetControlInterfaceCount(out uint count);
        [PreserveSig] int GetControlInterface(uint index, out IntPtr controlInterface);
        [PreserveSig] int EnumPartsIncoming(out IntPtr parts);
        [PreserveSig] int EnumPartsOutgoing(out IntPtr parts);
        [PreserveSig] int GetTopologyObject(out IDeviceTopology topology);
        [PreserveSig] int Activate(uint classContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int RegisterControlChangeCallback(ref Guid iid, IntPtr callback);
        [PreserveSig] int UnregisterControlChangeCallback(IntPtr callback);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsMethod(IntPtr method, uint methodLength, IntPtr data, uint dataLength, out uint bytesReturned);
        [PreserveSig] int KsEvent(IntPtr eventData, uint eventLength, IntPtr data, uint dataLength, out uint bytesReturned);
    }
}
