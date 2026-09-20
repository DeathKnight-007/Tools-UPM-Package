using SerializableReadWrite;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public interface IProtoNetClient<T>: IDisposable where T : struct
    {
        /// <summary>
        /// 当收到完整数据
        /// </summary>
        event Action<(T, object)> OnReceivedObject;

        /// <summary>
        /// 当收到一帧数据，一般一帧数据就是完整数据了，除非完整数据需要切片
        /// </summary>
        event Action<long> OnReceivedFrame;

        event Action<Exception> OnReceivedFail;

        event Action<Exception> OnSendFail;

        event Action OnConnected;

        event Action<Exception> OnConnectedFail;

        event Action OnDisconnected;

        int FrameBufferSize { get; }

        NetClient NetClient { get; }
        ISerializer Serializer { get; }

        long RequestId { get; }

        void Send<U>(T header, U value);

        /// <summary>
        /// 通过域名连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        Task Connect(DnsEndPoint dnsEndPoint);

        /// <summary>
        /// 通过ip连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        Task Connect(IPEndPoint ipEndPoint);

        /// <summary>
        /// 通过ip列表连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
       Task Connect(IPEndPoint[] ipEndPoints);

        /// <summary>
        /// 主动断开连接
        /// </summary>
        /// <returns></returns>
       void Disconnect();
    }
}
