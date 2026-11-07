using System;
using System.Runtime.InteropServices;
using System.Text;

using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR_WIN || ((UNITY_STANDALONE_WIN || UNITY_WSA) && !UNITY_EDITOR)
using ENetSocket = System.UIntPtr;
#else
using ENetSocket = System.Int32;
#endif

/// <summary>
/// ENet 1.3.18 的全部 53 个公开原生 API。Host、Peer、Packet 等原生对象以 IntPtr 表示。
/// size_t 使用 UIntPtr，以同时匹配 32 位和 64 位平台。
/// </summary>
public class ENetAPI
{
    // 以下成员加入现有 ENetAPI 类：
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void OnRuntimeStart()
    {
        // 先移除再注册，避免重复订阅。
        Application.quitting -= Shutdown;
        Application.quitting += Shutdown;

        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        if (initialized)
            return;

        if (enet_initialize() != 0)
            throw new InvalidOperationException("ENet 初始化失败。");

        initialized = true;
    }

    private static void Shutdown()
    {
        if (!initialized)
            return;

        // 在这里接入统一清理，顺序必须是：
        // 1. 停止并等待网络线程退出。
        // 2. 销毁所有 Host，释放应用仍持有的 Packet。
        // 3. 最后反初始化 ENet。

        enet_deinitialize();
        initialized = false;
    }

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    private static void RegisterEditorCallbacks()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
    }
#endif

    // 保留现有的静态链接方式。Android 的 .a 需要使用 IL2CPP。
    // 使用独立的 enet.dll 或 libenet.so 时，将库名改为 "enet"。
#if UNITY_EDITOR_WIN
    private const string libName = "enet";
#else
    private const string libName = "__Internal";
#endif

    public const uint ENET_VERSION_MAJOR = 1;
    public const uint ENET_VERSION_MINOR = 3;
    public const uint ENET_VERSION_PATCH = 18;
    public const uint ENET_VERSION = (ENET_VERSION_MAJOR << 16) | (ENET_VERSION_MINOR << 8) | ENET_VERSION_PATCH;
    public const uint ENET_HOST_ANY = 0;
    public const uint ENET_HOST_BROADCAST = uint.MaxValue;
    public const ushort ENET_PORT_ANY = 0;

#if UNITY_EDITOR_WIN || ((UNITY_STANDALONE_WIN || UNITY_WSA) && !UNITY_EDITOR)
    public static readonly ENetSocket ENET_SOCKET_NULL = new UIntPtr(UIntPtr.Size == 8 ? ulong.MaxValue : uint.MaxValue);
#else
    public const ENetSocket ENET_SOCKET_NULL = -1;
#endif

    public enum ENetSocketType
    {
        ENET_SOCKET_TYPE_STREAM = 1,
        ENET_SOCKET_TYPE_DATAGRAM = 2
    }

    [Flags]
    public enum ENetSocketWait : uint
    {
        ENET_SOCKET_WAIT_NONE = 0,
        ENET_SOCKET_WAIT_SEND = 1 << 0,
        ENET_SOCKET_WAIT_RECEIVE = 1 << 1,
        ENET_SOCKET_WAIT_INTERRUPT = 1 << 2
    }

    public enum ENetSocketOption
    {
        ENET_SOCKOPT_NONBLOCK = 1,
        ENET_SOCKOPT_BROADCAST = 2,
        ENET_SOCKOPT_RCVBUF = 3,
        ENET_SOCKOPT_SNDBUF = 4,
        ENET_SOCKOPT_REUSEADDR = 5,
        ENET_SOCKOPT_RCVTIMEO = 6,
        ENET_SOCKOPT_SNDTIMEO = 7,
        ENET_SOCKOPT_ERROR = 8,
        ENET_SOCKOPT_NODELAY = 9,
        ENET_SOCKOPT_TTL = 10
    }

    public enum ENetSocketShutdown
    {
        ENET_SOCKET_SHUTDOWN_READ = 0,
        ENET_SOCKET_SHUTDOWN_WRITE = 1,
        ENET_SOCKET_SHUTDOWN_READ_WRITE = 2
    }

    [Flags]
    public enum ENetPacketFlag : uint
    {
        ENET_PACKET_FLAG_RELIABLE = 1 << 0,
        ENET_PACKET_FLAG_UNSEQUENCED = 1 << 1,
        ENET_PACKET_FLAG_NO_ALLOCATE = 1 << 2,
        ENET_PACKET_FLAG_UNRELIABLE_FRAGMENT = 1 << 3,
        ENET_PACKET_FLAG_SENT = 1 << 8
    }

    public enum ENetEventType
    {
        ENET_EVENT_TYPE_NONE = 0,
        ENET_EVENT_TYPE_CONNECT = 1,
        ENET_EVENT_TYPE_DISCONNECT = 2,
        ENET_EVENT_TYPE_RECEIVE = 3
    }

    /// <summary>
    /// 使用enet_address_set_host将域名hostName: "www.baidu.com" 转换为uinthost
    /// 使用 enet_address_set_host_ip 将域名ip:"127.0.0.1"转换为uinthost
    /// 使用原生默认对齐，不要为这些结构体设置 Pack = 1。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ENetAddress
    {
        // host 使用网络字节序，port 使用主机字节序。
        public uint host;// 实际上是ip地址，ip地址本来就是32位的。域名也是经过dns解析成ip的
        public ushort port;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENetBuffer
    {
#if UNITY_EDITOR_WIN || ((UNITY_STANDALONE_WIN || UNITY_WSA) && !UNITY_EDITOR)
        // win32.h 与 unix.h 的字段顺序不同。
        public UIntPtr dataLength;
        public IntPtr data;
#else
        public IntPtr data;
        public UIntPtr dataLength;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENetPacket
    {
        public UIntPtr referenceCount;
        public ENetPacketFlag flags;
        public IntPtr data;
        public UIntPtr dataLength;
        public IntPtr freeCallback;
        public IntPtr userData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENetEvent
    {
        public ENetEventType type;
        public IntPtr peer;
        public byte channelID;
        public uint data;
        public IntPtr packet;
    }

    // 回调字段存放 Marshal.GetFunctionPointerForDelegate 得到的函数指针。
    // 委托必须在原生代码使用期间保持引用；IL2CPP 回调使用静态方法，
    // 并在方法上添加 [AOT.MonoPInvokeCallback(typeof(对应委托类型))]。
    [StructLayout(LayoutKind.Sequential)]
    public struct ENetCallbacks
    {
        public IntPtr malloc;
        public IntPtr free;
        public IntPtr no_memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENetCompressor
    {
        public IntPtr context;
        public IntPtr compress;
        public IntPtr decompress;
        public IntPtr destroy;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr ENetMallocCallback(UIntPtr size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ENetFreeCallback(IntPtr memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ENetNoMemoryCallback();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ENetPacketFreeCallback(IntPtr packet);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate UIntPtr ENetCompressCallback(IntPtr context, IntPtr inBuffers, UIntPtr inBufferCount, UIntPtr inLimit, IntPtr outData, UIntPtr outLimit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate UIntPtr ENetDecompressCallback(IntPtr context, IntPtr inData, UIntPtr inLimit, IntPtr outData, UIntPtr outLimit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ENetDestroyCallback(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate uint ENetChecksumCallback(IntPtr buffers, UIntPtr bufferCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ENetInterceptCallback(IntPtr host, IntPtr enetEvent);

    // 全局初始化与时间。返回值为 0 表示初始化成功。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_initialize();

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_initialize_with_callbacks(uint version, ref ENetCallbacks inits);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_deinitialize();

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static uint enet_linked_version();

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static uint enet_time_get();

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_time_set(uint newTimeBase);

    // 底层 Socket API。ENetSocket 在 Windows 为指针宽度的无符号整数，在 Android 为 int。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static ENetSocket enet_socket_create(ENetSocketType type);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_bind(ENetSocket socket, ref ENetAddress address);

    // address 传 IntPtr.Zero 表示任意本地地址和自动分配的端口。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_bind(ENetSocket socket, IntPtr address);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_get_address(ENetSocket socket, out ENetAddress address);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_listen(ENetSocket socket, int backlog);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static ENetSocket enet_socket_accept(ENetSocket socket, out ENetAddress address);

    // address 可以传 IntPtr.Zero。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static ENetSocket enet_socket_accept(ENetSocket socket, IntPtr address);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_connect(ENetSocket socket, ref ENetAddress address);

    // buffers 指向连续的 ENetBuffer 数组；address 为原生 ENetAddress 指针或 IntPtr.Zero。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_send(ENetSocket socket, IntPtr address, IntPtr buffers, UIntPtr bufferCount);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_send(ENetSocket socket, ref ENetAddress address, [In] ENetBuffer[] buffers, UIntPtr bufferCount);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_receive(ENetSocket socket, IntPtr address, IntPtr buffers, UIntPtr bufferCount);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_receive(ENetSocket socket, out ENetAddress address, [In, Out] ENetBuffer[] buffers, UIntPtr bufferCount);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_wait(ENetSocket socket, ref ENetSocketWait condition, uint timeout);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_set_option(ENetSocket socket, ENetSocketOption option, int value);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_get_option(ENetSocket socket, ENetSocketOption option, out int value);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socket_shutdown(ENetSocket socket, ENetSocketShutdown how);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_socket_destroy(ENetSocket socket);

    // readSet/writeSet 是平台原生 fd_set 指针，不能用同一个托管结构跨平台代替。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_socketset_select(ENetSocket maxSocket, IntPtr readSet, IntPtr writeSet, uint timeout);

    /// <summary>
    /// 将字符串ip解析为网络字节
    /// 地址。原生参数为 char*，不是 UTF-16；这里使用 ANSI，适用于 IP 和 ASCII/Punycode 域名。
    /// </summary>
    /// <param name="address"></param>
    /// <param name="hostName">ip "127.0.0.1"</param>
    /// <returns>0 解析成功， -1解析失败</returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_address_set_host_ip(ref ENetAddress address, [MarshalAs(UnmanagedType.LPStr)] string hostName);

    /// <summary>
    /// 将域名解析为网络字节
    /// </summary>
    /// <param name="address"></param>
    /// <param name="hostName">域名 "www.baidu.com"</param>
    /// <returns>0 解析成功， -1解析失败</returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_address_set_host(ref ENetAddress address, [MarshalAs(UnmanagedType.LPStr)] string hostName);

    // nameLength 是包含结尾 '\0' 在内的缓冲区字节容量，不应超过实际分配的容量。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_address_get_host_ip(ref ENetAddress address, [Out] StringBuilder hostName, UIntPtr nameLength);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_address_get_host(ref ENetAddress address, [Out] StringBuilder hostName, UIntPtr nameLength);

    // 数据包。NO_ALLOCATE 模式下，data 的地址必须保持有效，直到原生数据包释放。
    // 发送成功后由 ENet 管理 packet；收到的 packet 使用完后调用 enet_packet_destroy。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_packet_create(IntPtr data, UIntPtr dataLength, ENetPacketFlag flags);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_packet_destroy(IntPtr packet);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_packet_resize(IntPtr packet, UIntPtr dataLength);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static uint enet_crc32(IntPtr buffers, UIntPtr bufferCount);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static uint enet_crc32([In] ENetBuffer[] buffers, UIntPtr bufferCount);

    /// <summary>
    /// 创建客户端
    /// </summary>
    /// <param name="address">  nullptr,不指定本地绑定地址和端口 </param>
    /// <param name="peerCount">  1,最多管理 1 条连接，通常连接一个服务端 </param>
    /// <param name="channelLimit"> 2, 允许使用两个通道：0、1 </param>
    /// <param name="incomingBandwidth"> 0,不设置固定的接收带宽限制 </param>
    /// <param name="outgoingBandwidth"> 0 不设置固定的发送带宽限制 </param>
    /// <returns>ENetHost*</returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_host_create(IntPtr address, UIntPtr peerCount, UIntPtr channelLimit, uint incomingBandwidth, uint outgoingBandwidth);

    /// <summary>
    /// 创建客户端
    /// </summary>
    /// <param name="address">  ENetAddress </param>
    /// <param name="peerCount">  1,最多管理 1 条连接，通常连接一个服务端 </param>
    /// <param name="channelLimit"> 2, 允许使用两个通道：0、1 </param>
    /// <param name="incomingBandwidth"> 0,不设置固定的接收带宽限制 </param>
    /// <param name="outgoingBandwidth"> 0 不设置固定的发送带宽限制 </param>
    /// <returns>ENetHost*</returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_host_create(ref ENetAddress address, UIntPtr peerCount, UIntPtr channelLimit, uint incomingBandwidth, uint outgoingBandwidth);

    /// <summary>
    /// 释放host
    /// </summary>
    /// <param name="host"></param>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_destroy(IntPtr host);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="host">你已经创建的 enetHost</param>
    /// <param name="address">上一步准备的服务器地址</param>
    /// <param name="channelCount">请求使用的通道数量，你可以先用 2</param>
    /// <param name="data">	随连接请求附带的一个 uint 数据，暂时填 0；它不是超时时间</param>
    /// <returns>ENetPeer* 空指针无法连接 非空指针连接过程已经开始</returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_host_connect(IntPtr host, ref ENetAddress address, UIntPtr channelCount, uint data);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_host_check_events(IntPtr host, out ENetEvent enetEvent);

    /// <summary>
    /// 同步函数，未完成连接会阻塞
    /// </summary>
    /// <param name="host"></param>
    /// <param name="enetEvent"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_host_service(IntPtr host, out ENetEvent enetEvent, uint timeout);

    // enetEvent 可以传 IntPtr.Zero，用于只处理网络收发而不取出事件。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_host_service(IntPtr host, IntPtr enetEvent, uint timeout);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_flush(IntPtr host);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_broadcast(IntPtr host, byte channelID, IntPtr packet);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_compress(IntPtr host, ref ENetCompressor compressor);

    // compressor 传 IntPtr.Zero 可关闭压缩。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_compress(IntPtr host, IntPtr compressor);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_host_compress_with_range_coder(IntPtr host);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_channel_limit(IntPtr host, UIntPtr channelLimit);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_host_bandwidth_limit(IntPtr host, uint incomingBandwidth, uint outgoingBandwidth);

    // Peer。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static int enet_peer_send(IntPtr peer, byte channelID, IntPtr packet);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_peer_receive(IntPtr peer, out byte channelID);

    // channelID 可以传 IntPtr.Zero。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_peer_receive(IntPtr peer, IntPtr channelID);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_ping(IntPtr peer);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_ping_interval(IntPtr peer, uint pingInterval);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_timeout(IntPtr peer, uint timeoutLimit, uint timeoutMinimum, uint timeoutMaximum);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_reset(IntPtr peer);

    /// <summary>
    /// 发送队列中清空，立即开始断开握手，不等待，后续进度与结果由enet_host_service推进
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="data">填0</param>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_disconnect(IntPtr peer, uint data);

    /// <summary>
    /// 立即断开，并重置本地，尝试发送握手，但是不保证完成，不等待对端确认
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="data">填0</param>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_disconnect_now(IntPtr peer, uint data);

    /// <summary>
    /// 发送队列处理完，再开始断开握手，不等待，后续进度与结果由enet_host_service推进
    /// </summary>
    /// <param name="peer"></param>
    /// <param name="data">填0</param>
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_disconnect_later(IntPtr peer, uint data);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_peer_throttle_configure(IntPtr peer, uint interval, uint acceleration, uint deceleration);

    // Range coder。所有输入/输出数据缓冲区均由调用方分配并保证地址在调用期间有效。
    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static IntPtr enet_range_coder_create();

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static void enet_range_coder_destroy(IntPtr context);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static UIntPtr enet_range_coder_compress(IntPtr context, IntPtr inBuffers, UIntPtr inBufferCount, UIntPtr inLimit, IntPtr outData, UIntPtr outLimit);

    [DllImport(libName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public extern static UIntPtr enet_range_coder_decompress(IntPtr context, IntPtr inData, UIntPtr inLimit, IntPtr outData, UIntPtr outLimit);
}
