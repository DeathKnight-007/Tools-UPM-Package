using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public abstract class NetSimpleClient : IDisposable
    {
        /// <summary>
        /// 本地端点
        /// </summary>
        public abstract IPEndPoint ClientIPEndPoint { get; }
        
        /// <summary>
        /// 服务器端点
        /// </summary>
        public abstract IPEndPoint ServerIpEndPoint { get; }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public abstract void Init(NetClient.NetClientConfig config);

        /// <summary>
        /// 关闭释放所有资源
        /// </summary>
        public abstract void Dispose();

       /// <summary>
       /// 直连端点ip
       /// </summary>
       /// <param name="ip"></param>
       /// <param name="host">用于TLS证书验证，一般是域名，服务器的证书名字</param>
       /// <returns></returns>
        public abstract Task Connect(IPEndPoint ip);

        /// <summary>
        /// 主动断开连接
        /// </summary>
        public abstract void Disconnect();

        public abstract Task Send(byte[] buffer, int offset, int count, CancellationToken cancel);

        public abstract Task<int> Receive(byte[] buffer, int offset, CancellationToken cancel);
    }
}
