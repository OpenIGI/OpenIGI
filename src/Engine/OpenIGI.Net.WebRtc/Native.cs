using System.Runtime.InteropServices;

namespace OpenIGI.Net.WebRtc;

/// <summary>libdatachannel's C API (<c>rtc/rtc.h</c>, 0.23), as much of it as a data channel needs.</summary>
internal static unsafe partial class Native
{
    public const string Library = "datachannel";

    public const int LogWarning = 3;

    // rtcState.
    public const int StateConnected = 2;
    public const int StateDisconnected = 3;
    public const int StateFailed = 4;
    public const int StateClosed = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct Configuration
    {
        public nint IceServers;
        public int IceServersCount;
        public nint ProxyServer;
        public nint BindAddress;
        public int CertificateType;
        public int IceTransportPolicy;
        public byte EnableIceTcp;
        public byte EnableIceUdpMux;
        public byte DisableAutoNegotiation;
        public byte ForceMediaTransport;
        public ushort PortRangeBegin;
        public ushort PortRangeEnd;
        public int Mtu;
        public int MaxMessageSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Reliability
    {
        public byte Unordered;
        public byte Unreliable;
        public uint MaxPacketLifeTime;
        public uint MaxRetransmits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DataChannelInit
    {
        public Reliability Reliability;
        public nint Protocol;
        public byte Negotiated;
        public byte ManualStream;
        public ushort Stream;
    }

    [LibraryImport(Library, EntryPoint = "rtcInitLogger")]
    public static partial void InitLogger(int level, delegate* unmanaged[Cdecl]<int, byte*, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcCreatePeerConnection")]
    public static partial int CreatePeerConnection(Configuration* config);

    [LibraryImport(Library, EntryPoint = "rtcDeletePeerConnection")]
    public static partial int DeletePeerConnection(int pc);

    [LibraryImport(Library, EntryPoint = "rtcSetUserPointer")]
    public static partial void SetUserPointer(int id, nint pointer);

    [LibraryImport(Library, EntryPoint = "rtcSetLocalDescriptionCallback")]
    public static partial int SetLocalDescriptionCallback(int pc, delegate* unmanaged[Cdecl]<int, byte*, byte*, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetLocalCandidateCallback")]
    public static partial int SetLocalCandidateCallback(int pc, delegate* unmanaged[Cdecl]<int, byte*, byte*, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetStateChangeCallback")]
    public static partial int SetStateChangeCallback(int pc, delegate* unmanaged[Cdecl]<int, int, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetDataChannelCallback")]
    public static partial int SetDataChannelCallback(int pc, delegate* unmanaged[Cdecl]<int, int, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetRemoteDescription", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int SetRemoteDescription(int pc, string sdp, string type);

    [LibraryImport(Library, EntryPoint = "rtcAddRemoteCandidate", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int AddRemoteCandidate(int pc, string candidate, string? mid);

    [LibraryImport(Library, EntryPoint = "rtcCreateDataChannelEx", StringMarshalling = StringMarshalling.Utf8)]
    public static partial int CreateDataChannel(int pc, string label, DataChannelInit* init);

    [LibraryImport(Library, EntryPoint = "rtcSetOpenCallback")]
    public static partial int SetOpenCallback(int id, delegate* unmanaged[Cdecl]<int, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetClosedCallback")]
    public static partial int SetClosedCallback(int id, delegate* unmanaged[Cdecl]<int, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetErrorCallback")]
    public static partial int SetErrorCallback(int id, delegate* unmanaged[Cdecl]<int, byte*, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSetMessageCallback")]
    public static partial int SetMessageCallback(int id, delegate* unmanaged[Cdecl]<int, byte*, int, nint, void> callback);

    [LibraryImport(Library, EntryPoint = "rtcSendMessage")]
    public static partial int SendMessage(int id, byte* data, int size);

    [LibraryImport(Library, EntryPoint = "rtcGetSelectedCandidatePair")]
    public static partial int GetSelectedCandidatePair(int pc, byte* local, int localSize, byte* remote, int remoteSize);

    [LibraryImport(Library, EntryPoint = "rtcDeleteDataChannel")]
    public static partial int DeleteDataChannel(int dc);

    public static string? Text(byte* text) => text is null ? null : Marshal.PtrToStringUTF8((nint)text);
}
