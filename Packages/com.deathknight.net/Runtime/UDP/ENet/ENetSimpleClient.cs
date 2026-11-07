using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static ENetAPI;

namespace DeathKnight.Net
{
    public class ENetSimpleClient : IENetSimpleClient
    {
        public IPEndPoint ServerIpEndPoint { get; private set; }
        public async Task Connect(IPEndPoint ip, CancellationToken token)
        {
            ENetAddress address = ParseENetAddress(ip);
            await Connect(address, token);
        }

        public async Task Connect(string host, int port, CancellationToken token)
        {
            ENetAddress address = ParseENetAddress(host, port);
            await Connect(address, token);
        }

        private async Task Connect(ENetAddress address, CancellationToken token)
        {
            enetPeer = enet_host_connect(enetHost, ref address, new UIntPtr(2), 0);
            if (enetPeer == IntPtr.Zero)
            {
                throw new Exception("can't create peer");
            }
            ENetEvent enet_event = default(ENetEvent);
            uint timeout = 10 * 1000;
            await Task.Run(() =>
            {
                long startTime = DateTime.UtcNow.Ticks / 10000;
                while (DateTime.UtcNow.Ticks / 10000 - startTime < timeout)
                {
                    // 请求取消，抛出取消异常
                    if (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }
                    uint onetimeout = 20;
                    int result = enet_host_service(enetHost, out enet_event, onetimeout);
                    if (result < 0)
                    {
                        throw new Exception("receive error");
                    }
                    else if (result == 0)
                    {

                    }
                    else
                    {
                        if (enet_event.type == ENetEventType.ENET_EVENT_TYPE_CONNECT)
                        {
                            ServerIpEndPoint = new IPEndPoint(address.host, address.port);
                            return;
                        }
                        else if (enet_event.type == ENetEventType.ENET_EVENT_TYPE_DISCONNECT)
                        {
                            throw new Exception("connect fail");
                        }
                        else
                        {
                            throw new Exception("connect fail");
                        }
                    }
                }
                throw new TimeoutException("连接超过10s了");
            });
        }

        public void Disconnect()
        {
            enet_peer_disconnect(enetPeer, 0);
        }

        private bool disposed = false;
        public void Dispose()
        {
            if (disposed)
                return;
            enet_host_destroy(enetHost);
            disposed = true;
        }

        private NetClientConfig config;
        private IntPtr enetHost;
        private IntPtr enetPeer;
        public void Init(NetClientConfig config)
        {
            this.config = config;
            enetHost = enet_host_create(IntPtr.Zero, new UIntPtr(1), new UIntPtr(2), 0, 0);
            if(enetHost == IntPtr.Zero)
            {
                throw new Exception("create enetHost fail");
            }
        }

        private ENetAddress ParseENetAddress(IPEndPoint ip)
        {
            ENetAddress address = new ENetAddress();
            int result = enet_address_set_host_ip(ref address, ip.Address.ToString());
            if (result < 0)
                throw new Exception($"address parse fail. ip:{ip.Address}, port:{ip.Port}");
            address.port = (ushort)ip.Port;
            return address;
        }

        private ENetAddress ParseENetAddress(string host, int port)
        {
            ENetAddress address = new ENetAddress();
            int result = enet_address_set_host_ip(ref address, host);
            if (result < 0)
                throw new Exception($"address parse fail. host:{host}, port:{port}");
            address.port = (ushort)port;
            return address;
        }

        public int Receive(byte[] buffer, int offset, int count, out ENetPacketFlag flag)
        {
            flag = ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE;
            ENetEvent enet_event;
            int result = enet_host_service(enetHost, out enet_event, 0);
            if (result < 0)
            {
                throw new Exception("receive error");
            }
            else if (result == 0)
            {
                return 0;
            }
            else
            {
                if (enet_event.channelID == 0)
                {
                    flag = ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE;
                }
                else if (enet_event.channelID == 1)
                {
                    flag = ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED;
                }
                else
                {
                    throw new Exception("not support ENetPacketFlag:" + flag);
                }
                switch (enet_event.type)
                {
                    case ENetEventType.ENET_EVENT_TYPE_NONE:
                        return 0;
                    case ENetEventType.ENET_EVENT_TYPE_CONNECT:
                        return 0;
                    case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
                        throw new EndOfStreamException();
                    case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
                        try
                        {
                            // 接收数据消息
                            ENetPacket packet = Marshal.PtrToStructure<ENetPacket>(enet_event.packet); //数据还在原生内存里
                            int receiveCount = (int)packet.dataLength;
                            if (receiveCount > count)
                                throw new ArgumentOutOfRangeException();
                            Marshal.Copy(packet.data, buffer, offset, receiveCount);
                            return receiveCount;
                        }
                        catch
                        {
                            throw;
                        }
                        finally
                        {
                            enet_packet_destroy(enet_event.packet);
                        }
                    default:
                        return 0;

                }
            }
        }

        public void Send(MemoryStream stream, int count, ENetPacketFlag flag)
        {
            if (stream.TryGetBuffer(out var segment))
            {
                Send(segment.Array, segment.Offset + (int)stream.Position, count, flag);
            }
        }

        public void Send(byte[] buffer, int offset, int count, ENetPacketFlag flag)
        {
            if (flag != ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE && flag != ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                throw new Exception("flag is not supported");
            // 将buffer数据复制进enet发送队列里
            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                IntPtr bufferPtr = handle.AddrOfPinnedObject();
                IntPtr packet = enet_packet_create(bufferPtr + offset, new UIntPtr((uint)count), flag);
                if (packet == IntPtr.Zero)
                    throw new Exception("数据包创建失败");
                byte channel = 0;
                if(flag == ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE)
                {
                    channel = 0;
                }
                else if(flag == ENetPacketFlag.ENET_PACKET_FLAG_UNSEQUENCED)
                {
                    channel = 1;
                }
                int result = enet_peer_send(enetPeer, channel, packet);
                if (result < 0)
                    throw new Exception("消息入队失败");
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
