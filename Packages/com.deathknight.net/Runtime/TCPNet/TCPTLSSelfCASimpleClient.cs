using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static UnityEditorInternal.ReorderableList;

namespace DeathKnight.Net
{
    /// <summary>
    /// 可以自定义加载受信任证书，去校验服务器证书，则服务器证书可以由自己受信任的证书颁发
    /// </summary>
    public class TCPTLSSelfCASimpleClient : NetSimpleClient
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
        /// <summary>
        /// 验证服务器证书
        /// </summary>
        /// <param name="sender">当前 SslStream</param>
        /// <param name="serverCertificate">服务器叶子证书</param>
        /// <param name="serverChain">系统使用默认信任库尝试构建的服务器证书链</param>
        /// <param name="sslPolicyErrors">系统证书验证结果</param>
        /// <returns>服务器证书是否可信</returns>
        private bool RemoteCertificateValidationCallback(object sender, X509Certificate serverCertificate, X509Chain serverChain, SslPolicyErrors sslPolicyErrors)
        {
            if (serverCertificate == null ||
                (sslPolicyErrors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            {
                return false;
            }

            // 自定义链构建只负责验证签发关系，服务器名称必须继续使用
            // SslStream 根据 TargetHost 生成的验证结果，不能忽略名称不匹配。
            if ((sslPolicyErrors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                return false;
            }
            X509Certificate2Collection trustedRootCertificates = LoadTrustedRootCertificates();
            if (trustedRootCertificates == null || trustedRootCertificates.Count == 0)
            {
                return false;
            }

            try
            {
                // 始终复制回调传入的证书，不接管 SslStream 所拥有对象的生命周期。
                using (X509Certificate2 leafCertificate = new X509Certificate2(serverCertificate.GetRawCertData()))
                using (X509Chain privateChain = new X509Chain())
                {
                    X509ChainPolicy policy = privateChain.ChainPolicy;
                    // Unity 2022.3 使用的旧版 API 没有 CustomRootTrust。
                    // 先允许“未知根”参与构链，再在 Build 后手动把链终点固定到加载器提供的根 CA。
                    policy.ExtraStore.AddRange(trustedRootCertificates);
                    policy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                    policy.RevocationMode = X509RevocationMode.NoCheck;

                    // serverChain 中可能包含服务器发送的中间 CA，也可能包含本机缓存补充的证书。
                    // 它们只作为构链候选，不能放进 CustomTrustStore 变成信任锚。
                    if (serverChain != null)
                    {
                        foreach (X509ChainElement element in serverChain.ChainElements)
                        {
                            if (!CertificatesEqual(element.Certificate, leafCertificate))
                            {
                                policy.ExtraStore.Add(element.Certificate);
                            }
                        }
                    }

                    if (!privateChain.Build(leafCertificate))
                    {
                        foreach (X509ChainStatus status in privateChain.ChainStatus)
                        {
                            Debug.LogWarning($"服务器证书链验证失败：{status.Status}，{status.StatusInformation}");
                        }
                        return false;
                    }

                    // AllowUnknownCertificateAuthority 只能用于容纳私有根导致的 UntrustedRoot。
                    // 任何其他链错误仍然拒绝，例如过期、用途错误、签名错误或链不完整。
                    if (!HasOnlyAllowedChainStatuses(privateChain) ||
                        privateChain.ChainElements.Count == 0 ||
                        !ContainsCertificate(
                            trustedRootCertificates,
                            privateChain.ChainElements[privateChain.ChainElements.Count - 1].Certificate))
                    {
                        return false;
                    }

                    SaveValidatedServerChain(privateChain);
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is CryptographicException ||
                exception is PlatformNotSupportedException)
            {
                Debug.LogWarning($"验证服务器证书失败：{exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// 返回客户端明确信任的私有根 CA 集合。
        /// 集合中只能放根 CA，不能放服务器叶子证书或中间 CA。
        /// </summary>
        public Func<X509Certificate2Collection> X509CertificatesLoader { get; set; }

        /// <summary>
        /// 加载本地受信任的根证书们
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private X509Certificate2Collection LoadTrustedRootCertificates()
        {
            if (X509CertificatesLoader == null)
            {
                throw new InvalidOperationException("X509CertificatesLoader is not configured.");
            }

            X509Certificate2Collection certificates = X509CertificatesLoader();

            if (certificates == null)
            {
                throw new InvalidOperationException("X509CertificatesLoader returned null.");
            }

            if (certificates.Count == 0)
            {
                throw new InvalidOperationException("X509CertificatesLoader did not load any trusted root CA certificate.");
            }

            foreach (X509Certificate2 certificate in certificates)
            {
                // 验证证书是否有颁发证书的能力
                if (!IsCertificateAuthority(certificate))
                {
                    throw new InvalidOperationException(
                        $"Trusted certificate is not a CA certificate: {certificate.Subject}");
                }

                // 主体是自己，颁发者也是自己，才说明它是一个根证书
                if (!ByteArraysEqual(certificate.SubjectName.RawData, certificate.IssuerName.RawData))
                {
                    throw new InvalidOperationException(
                        $"X509CertificatesLoader must load a self-signed root CA certificate: {certificate.Subject}");
                }
            }

            return certificates;
        }

        private static bool IsCertificateAuthority(X509Certificate2 certificate)
        {
            foreach (X509Extension extension in certificate.Extensions)
            {
                X509BasicConstraintsExtension basicConstraints = extension as X509BasicConstraintsExtension;
                if (basicConstraints != null)
                {
                    return basicConstraints.CertificateAuthority;
                }
            }

            return false;
        }

        private static bool ContainsCertificate(X509Certificate2Collection certificates, X509Certificate certificate)
        {
            foreach (X509Certificate2 candidate in certificates)
            {
                if (CertificatesEqual(candidate, certificate))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasOnlyAllowedChainStatuses(X509Chain chain)
        {
            foreach (X509ChainStatus status in chain.ChainStatus)
            {
                if (status.Status != X509ChainStatusFlags.NoError &&
                    status.Status != X509ChainStatusFlags.UntrustedRoot)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CertificatesEqual(X509Certificate first, X509Certificate second)
        {
            return first != null &&
                second != null &&
                ByteArraysEqual(first.GetRawCertData(), second.GetRawCertData());
        }

        private static bool ByteArraysEqual(byte[] first, byte[] second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }

            if (first == null || second == null || first.Length != second.Length)
            {
                return false;
            }

            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        private void SaveValidatedServerChain(X509Chain chain)
        {
            X509Certificate2Collection certificates = new X509Certificate2Collection();
            foreach (X509ChainElement element in chain.ChainElements)
            {
                certificates.Add(new X509Certificate2(element.Certificate.RawData));
            }

            config.encryptInfo.ServerCertificates = certificates;
        }

        public override async Task Connect(IPEndPoint ip)
        {
            this.serverIpEndPoint = ip;
            if (tcpClient == null)
            {
                throw new SocketException(-1);
            }
            if (config.encryptInfo.EncryptStrategy != NetClient.EncryptStrategy.TLS_SELF_CA)
            {
                throw new ArgumentException("EncryptStrategy is not TLS_SELF_CA, but use TCPTLSSelfCASimpleClient, current EncryptStrategy is:" + config.encryptInfo.EncryptStrategy.ToString());
            }

            if (string.IsNullOrWhiteSpace(config.encryptInfo.ServerAlternativeName))
            {
                throw new ArgumentException("ServerAlternativeName must be configured for server certificate name validation.");
            }

            // 加载操作放在握手回调之外，避免同步证书回调执行磁盘或资源读取。
            LoadTrustedRootCertificates();
            config.encryptInfo.ServerCertificates = new X509Certificate2Collection();

            await tcpClient.ConnectAsync(ip.Address, ip.Port);
            // 使用TLS加密的话，连接完成后要走加密流程
            SslStream sslStream = new SslStream(tcpClient.GetStream(), false);
            SslClientAuthenticationOptions options = new SslClientAuthenticationOptions();
            // 设置服务器证书可选名字
            options.TargetHost = config.encryptInfo.ServerAlternativeName;
            // 选择TLS协议版本，一般是Tls12，1.2版本，其他的选项基本已经废弃
            options.EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            // 选择加密算法, 目前支持的版本，不能选择加密算法。只能跟服务器协商了
            options.EncryptionPolicy = EncryptionPolicy.RequireEncryption;
            // 收到服务器证书后的验证函数,可以自定义自己的根CA证书
            options.RemoteCertificateValidationCallback = RemoteCertificateValidationCallback;
            CancellationTokenSource c = new CancellationTokenSource();
            // 可自定义部分
            // 1、选择TLS协议
            // 2、这版本api不能选择加密算法
            // 3、收到服务器证书后的验证函数,可以自定义自己的根CA证书
            await sslStream.AuthenticateAsClientAsync(options, c.Token);
            // 完成标准TLS流程，所有过程都是TLS协议里定义的，可以和任何语言无缝对接
            // 1、客户端say hello: 生成临时公钥/私钥对，将公钥和支持的TLS版本们，和支持的加密算法们发送给服务器
            // 2、服务器say hello: 生成临时公钥/私钥对, 将公钥，和最后选择的TLS版本，加密算法发送给客户端
            // 3、服务器使用，客户端公钥+自己私钥派生出密钥。客户端使用，服务器公钥+自己私钥派生出密钥。两者生成的密钥是一样的。
            // 4、客户端发送finished密钥，服务器收到finished密钥，确认密钥确实一样。同时发送给客户端自己的finished密钥，客户端再校验一遍。
            // 5、使用密钥对称加密算法加密服务器证书，发送给客户端
            // 6、客户端验证服务器证书，完成TLS连接
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
