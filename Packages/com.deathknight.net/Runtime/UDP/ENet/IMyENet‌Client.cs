using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using System;
using SerializableReadWrite;
using static ENetAPI;

namespace DeathKnight.Net
{
    public interface IMyENet‌Client<T> : IDisposable where T : BaseProto, new()
    {
        /// <summary>
        /// 服务器端点
        /// </summary>
        IPEndPoint ServerIpEndPoint { get; }

        /// <summary>
        /// 在主线程发布该事件，T为消息头， object为消息体
        /// </summary>
        event Action<(T, object)> OnReceivedObject;

        /// <summary>
        /// 在主线程发布该事件，long为RequestId
        /// </summary>
        event Action<long> OnReceivedFrame;

        /// <summary>
        /// 接收失败
        /// </summary>
        event Action<Exception> OnReceivedFail;

        /// <summary>
        /// 发送失败
        /// </summary>
        event Action<Exception> OnSendFail;

        /// <summary>
        /// 连接成功
        /// </summary>
        event Action OnConnected;

        /// <summary>
        /// 连接失败
        /// </summary>
        event Action<Exception> OnConnectedFail;

        /// <summary>
        /// 客户端和服务端都断开连接了
        /// </summary>
        event Action OnDisconnected;

        /// <summary>
        /// 每帧数据缓存大小
        /// </summary>
        int FrameBufferSize { get; }

        /// <summary>
        /// 序列化工具
        /// </summary>
        ISerializer Serializer { get; }

        /// <summary>
        /// 配置
        /// </summary>
        NetClientConfig Config { get; }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        void Init(NetClientConfig config, int frameBufferSize, ISerializer Serializer);

        /// <summary>
        /// 直连端点ip
        /// </summary>
        /// <param name="ip"></param>
        /// <param name="host">用于TLS证书验证，一般是域名，服务器的证书名字</param>
        /// <returns></returns>
        Task Connect(IPEndPoint ip);

        /// <summary>
        /// 连接域名
        /// </summary>
        /// <param name="host"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        Task Connect(string host, int port);

        /// <summary>
        /// 不再发送消息，并给服务器发送Fin
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 主线程将要发送的消息，进入发送队列
        /// </summary>
        /// <typeparam name="U"></typeparam>
        /// <param name="data"></param>
        /// <param name="header"></param>
        void Send<U>(U data, T header, ENetPacketFlag flag);

        /// <summary>
        /// 每帧更新调用
        /// </summary>
        void Update();
    }
}
