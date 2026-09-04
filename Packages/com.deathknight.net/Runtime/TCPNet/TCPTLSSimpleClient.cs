using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static UnityEditorInternal.ReorderableList;

namespace DeathKnight.Net
{
    /// <summary>
    /// 标准TLS协议连接，自己操作系统查找受信任证书，校验服务器证书，则服务器证书必须是官方机构颁发的
    /// </summary>
    public class TCPTLSSimpleClient : NetSimpleClient
    {
        private IPEndPoint clientIPEndPoint;

        public override IPEndPoint ClientIPEndPoint
        {
            get
            {
                return clientIPEndPoint;
            }
        }

        private IPEndPoint serverIpEndPoint;
        public override IPEndPoint ServerIpEndPoint
        {
            get
            {
                return serverIpEndPoint;
            }
        }

        public override async Task Connect(IPEndPoint ip)
        {
            this.serverIpEndPoint = ip;
            if (tcpClient == null)
            {
                throw new SocketException(-1);
            }
            if(config.encryptInfo.EncryptStrategy != NetClient.EncryptStrategy.TLS)
            {
                throw new ArgumentException("EncryptStrategy is not TLS, but use TCPTLSSimpleClient, current EncryptStrategy is:" + config.encryptInfo.EncryptStrategy.ToString());
            }
            await tcpClient.ConnectAsync(ip.Address, ip.Port);
            // 使用TLS加密的话，连接完成后要走加密流程
            SslStream sslStream = new SslStream(tcpClient.GetStream(), false);
            await sslStream.AuthenticateAsClientAsync(config.encryptInfo.ServerAlternativeName);
            // 完成标准TLS流程，所有过程都是TLS协议里定义的，可以和任何语言无缝对接
            // 1、客户端say hello: 生成临时公钥/私钥对，将公钥和支持的TLS版本们，和支持的加密算法们发送给服务器
            // 2、服务器say hello: 生成临时公钥/私钥对, 将公钥，和最后选择的TLS版本，加密算法发送给客户端
            // 3、服务器使用，客户端公钥+自己私钥派生出密钥。客户端使用，服务器公钥+自己私钥派生出密钥。两者生成的密钥是一样的。
            // 4、客户端发送finished密钥，服务器收到finished密钥，确认密钥确实一样。同时发送给客户端自己的finished密钥，客户端再校验一遍。
            // 5、使用密钥对称加密算法加密服务器证书，发送给客户端
            // 6、客户端验证服务器证书，完成TLS连接，后续所有消息都经过了堆成密钥加密，包括前面的证书。
            this.clientIPEndPoint = tcpClient.Client.LocalEndPoint as IPEndPoint;
        }

        public override void Disconnect()
        {
            Dispose();
        }

        public override async Task Send(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
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

            if (config.WriteTimeout > 0)
            {
                using CancellationTokenSource timeoutCancelSource = new CancellationTokenSource();
                timeoutCancelSource.CancelAfter(config.WriteTimeout);
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
                    throw new OperationCanceledException("tcp write timeout", exception);
                }
            }
            else
            {
                await tcpClient.GetStream().WriteAsync(buffer, offset, count, cancel);
            }
        }

        public override void Dispose()
        {
            if (tcpClient == null)
            {
                return;
            }

            try
            {
                // 这里会操作系统会通知服务器断开连接
                tcpClient.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
            finally
            {
                tcpClient = null;
                clientIPEndPoint = null;
            }
        }

        private TcpClient tcpClient;
        private NetClient.NetClientConfig config;
        public override void Init(NetClient.NetClientConfig config)
        {
            this.config = config;
            tcpClient = new TcpClient();
            //tcpClient.ReceiveTimeout = config.ReceiveTimeout; 使用异步方式接收，这个参数tcpClient实际上没用
            if (config.OSReceiveBufferSize > 0)
            {
                tcpClient.ReceiveBufferSize = config.OSReceiveBufferSize;
            }
            //tcpClient.SendTimeout = config.WriteTimeout; 使用异步方式发送，这个参数tcpClient实际上没用
            if (config.OSSendBufferSize > 0)
            {
                tcpClient.SendBufferSize = config.OSSendBufferSize;
            }
        }

        public override async Task<int> Receive(byte[] buffer, int offset, CancellationToken cancel)
        {
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

            if (config.ReceiveTimeout > 0)
            {
                using CancellationTokenSource timeoutCancelSource = new CancellationTokenSource();
                timeoutCancelSource.CancelAfter(config.ReceiveTimeout);
                using CancellationTokenSource linkedCancelSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutCancelSource.Token, cancel);

                try
                {
                    // 不会停下这个任务，除非一下情况发生
                    // 1、收到了至少一个字节
                    // 2、主动关闭了连接
                    // 3、发生网络错误
                    // 网络看起来正常，但是接收不到网络数据，或者接收速度很慢，或者服务器坏了，这就需要超时设置
                    return await tcpClient.GetStream().ReadAsync(buffer, offset, buffer.Length, linkedCancelSource.Token);
                }
                catch (OperationCanceledException exception) when (timeoutCancelSource.IsCancellationRequested && !cancel.IsCancellationRequested)
                {
                    throw new TimeoutException("tcp receive timed out", exception);
                }
            }
            else
            {
                return await tcpClient.GetStream().ReadAsync(buffer, offset, buffer.Length - offset, cancel);
            }
        }
    }
}
