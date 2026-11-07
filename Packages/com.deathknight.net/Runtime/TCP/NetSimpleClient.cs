using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public interface NetSimpleClient : IDisposable
    {
        /// <summary>
        /// 本地端点
        /// </summary>
        IPEndPoint ClientIPEndPoint { get; }

        /// <summary>
        /// 服务器端点
        /// </summary>
        IPEndPoint ServerIpEndPoint { get; }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
       void Init(NetClientConfig config);

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

        Task Send(byte[] buffer, int offset, int count, CancellationToken cancel);

        Task<int> Receive(byte[] buffer, int offset, int count, CancellationToken cancel);
    }
}
