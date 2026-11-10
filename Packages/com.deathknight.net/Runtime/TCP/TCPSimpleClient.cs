using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    /// <summary>
    /// 无加密tcp连接
    /// </summary>
    public class TCPSimpleClient : NetSimpleClient
    {

        public IPEndPoint ClientIPEndPoint { get; private set; }

        public IPEndPoint ServerIpEndPoint { get; private set; }

        public async Task Connect(IPEndPoint ip)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (tcpClient == null)
            {
                throw new SocketException(-1);
            }
            await tcpClient.ConnectAsync(ip.Address, ip.Port);
            if (tcpClient != null)
            {
                this.ServerIpEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                this.ClientIPEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;
            }
        }

        public async Task Connect(string host, int port)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (tcpClient == null)
            {
                throw new SocketException(-1);
            }
            await tcpClient.ConnectAsync(host, port);
            if (tcpClient != null)
            {
                this.ServerIpEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
                this.ClientIPEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;
            }
        }

        public void Disconnect()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            tcpClient.Client.Shutdown(SocketShutdown.Send);
        }

        public async Task Send(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0 || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (tcpClient == null)
            {
                throw new InvalidOperationException("TCP client is not initialized or has been disconnected.");
            }

            using CancellationTokenSource timeoutCancelSource = new CancellationTokenSource();
            timeoutCancelSource.CancelAfter(config.WriteTimeout);
            timeoutCancelSource.Token.Register(() =>
            {
                this.Dispose();
            });
            using CancellationTokenSource linkedCancelSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCancelSource.Token, cancel);
            try
            {
                // 不会停下这个任务，除非以下情况发生
                // 1、网络不可用， 直接报异常结束
                // 2、socket关闭了，直接报异常结束

                // 看起来网络正常，但是数据发送不了，或者发送超级慢，或者干脆服务器坏了，这就需要超时设置了
                // 这个任务完成，只表示数据交给了操作系统，并不是表示数据已经成功发送出去了
                await tcpClient.GetStream().WriteAsync(buffer, offset, count, linkedCancelSource.Token);
            }
            catch (OperationCanceledException exception) when (timeoutCancelSource.IsCancellationRequested && !cancel.IsCancellationRequested)
            {
                throw new TimeoutException("WriteTimeout", exception);
            }

        }

        private bool disposed;
        private object selflock = new();
        public void Dispose()
        {
            lock (selflock)
            {
                if (disposed)
                    return;
                disposed = true;

                try
                {
                    tcpClient?.Dispose(); // 先中断底层网络读取
                    tcpClient = null;
                }
                finally
                {
                    ClientIPEndPoint = null;
                }
            }
        }

        private TcpClient tcpClient;
        private NetClientConfig config;
        private bool inited = false;
        public void Init(NetClientConfig config)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (inited)
                return;
            this.config = config;
            tcpClient = new TcpClient();
            inited = true;
        }

        public async Task<int> Receive(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ENetSimpleClient));
            if (!inited)
                throw new Exception("not inited");
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }


            if (tcpClient == null)
            {
                throw new InvalidOperationException("TCP client is not initialized or has been disconnected.");
            }


            using CancellationTokenSource timeoutCancelSource = new CancellationTokenSource();
            timeoutCancelSource.CancelAfter(config.ReceiveTimeout);
            timeoutCancelSource.Token.Register(() =>
            {
                this.Dispose();
            });
            using CancellationTokenSource linkedCancelSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCancelSource.Token, cancel);

            try
            {
                // 不会停下这个任务，除非一下情况发生
                // 1、收到了至少一个字节
                // 2、主动关闭了连接
                // 3、发生网络错误
                // 网络看起来正常，但是接收不到网络数据，或者接收速度很慢，或者服务器坏了，这就需要超时设置
                return await tcpClient.GetStream().ReadAsync(buffer, offset, count, linkedCancelSource.Token);
            }
            catch (OperationCanceledException exception) when (timeoutCancelSource.IsCancellationRequested && !cancel.IsCancellationRequested)
            {
                throw new TimeoutException("ReceiveTimeout", exception);
            }
        }
    }
}
