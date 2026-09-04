using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using static DeathKnight.Net.NetServer;

namespace DeathKnight.Net
{
    public abstract class NetClient : IDisposable
    {
        public enum IPsTryConnectStrategy
        {
            /// <summary>
            /// 如果是连接一个ip数组，直接使用第一个
            /// </summary>
            UseFirst = 0,
            /// <summary>
            /// 从一个ip数组中交错使用ipv4和ipv6ip，尝试，谁先连上(如果使用了TLC，则也经过了TLC验证)用谁
            /// </summary>
            HappyEyeballs = 1,
        }
        public enum EncryptStrategy
        {
            /// <summary>
            /// 不使用加密
            /// </summary>
            None = 0,
            /// <summary>
            /// 标准TLS协议流程,官方机构颁发证书
            /// </summary>
            TLS = 1,
            /// <summary>
            /// 私有颁发证书
            /// </summary>
            TLS_SELF_CA = 2,
        }
        public struct EncryptInfo
        {
            /// <summary>
            /// 加密策略选择
            /// </summary>
            public EncryptStrategy EncryptStrategy;

            /// <summary>
            /// 服务器证书中的某个可选名字
            /// </summary>
            public string ServerAlternativeName;

            /// <summary>
            /// 加载本地受信任根CA证书
            /// </summary>
            public Func<X509Certificate2Collection> X509CertificatesLoader;

            /// <summary>
            /// 服务器下发的证书链，用于验证服务器身份
            /// </summary>
            public X509Certificate2Collection ServerCertificates;
        }
        public struct NetClientAdvancedConfig
        {

            public int OSReceiveBufferSize;


            public int OSSendBufferSize;
        }
        public struct NetClientConfig
        {
            /// <summary>
            /// ip池选择策略
            /// </summary>
            public IPsTryConnectStrategy ipsTryConnectStrategy;

            /// <summary>
            /// 消息加密设置
            /// </summary>
            public EncryptInfo encryptInfo;

            /// <summary>
            /// 接收超时设置,即多长时间没有读到数据,<=0 表示不设置超时，这一层一般设置为0
            /// </summary>
            public int ReceiveTimeout;

            /// <summary>
            /// 操作系统接收缓冲字节长度， <=0 表示使用系统默认值
            /// 但是不代表一次只能接收这么多，一次接收很长就需要等待。缓冲长度影响接收效率和内存占用
            /// </summary>
            public int OSReceiveBufferSize;

            /// <summary>
            /// 发送超时设置，递交给操作系统数据的时长，影响这个时长的一般就是，发送慢，缓存满了得一直等待，<=0 表示不设置超时
            /// </summary>
            public int WriteTimeout;

            /// <summary>
            /// 操作系统发送缓冲字节长度, <=0 表示使用系统默认值
            /// 但是不代表一次只能发送这么多，一次发送很长就需要等待。缓冲长度影响发送效率和内存占用
            /// </summary>
            public int OSSendBufferSize;                   
        }
        public struct NetClientInfo
        {
            //public EndPoint endpoint;// 端点基类
            // 重点 AddressFamily，常用端点协议，InterNetwork, InterNetworkV6, Unspecified(ipv1,ipv6都不指定) Bluetooth, 其他基本不用
            
            /// <summary>
            /// 服务器域名
            /// </summary>
            public DnsEndPoint ServerDnsEndPoint;
            // AddressFamily继承，string Host, int Port
            //IPAddress[] ips = Dns.GetHostAddresses("www.baidu.com");
            // 域名解析过程，是操作系统在做，首先找本机缓存，再次去外网问去（dns域名解析系统，根服务器，com服务器等等分级缓存数据）
            // dns服务器，帮忙询问域名与ip的解析。他可以有自己的查询缓存，也可以有自己的权威数据，也可以去别的dns服务器询问
            // 局域网中，dns一般是网关，dns服务器一般是路由器，但是电脑也可以作为dns服务器，但是需要安装dns server程序，默认是有dns client程序的
            // 自己搭建dns服务器的需求：1、Jenkins不用绑定ip了直接绑定域名，换电脑也不用改ip了  2、机器多需要集中管理
            
            /// <summary>
            /// 服务器ip地址List
            /// </summary>
            public List<IPEndPoint> ServerIpEndPoints;
            // AddressFamily继承, int Port, IPAddress.TryParse("192.168.1.123")

            /// <summary>
            /// 正在连接中的ip
            /// </summary>
            public IPEndPoint ServerConnectedIP;
        }

        public abstract NetClientConfig DefaultConfig { get; }
        public abstract NetClientConfig Config { get; }
        public abstract NetClientInfo Info { get; }

        /// <summary>
        /// 实际连接实例
        /// </summary>
        public abstract NetSimpleClient SimpleClient { get; }
        public abstract void Init(NetClientConfig config);

        /// <summary>
        /// 通过域名连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public abstract Task Connect(DnsEndPoint dnsEndPoint);

        /// <summary>
        /// 通过ip连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public abstract Task Connect(IPEndPoint ipEndPoint);

        /// <summary>
        /// 通过ip列表连接服务器
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public abstract Task Connect(IPEndPoint[] ipEndPoints);

        /// <summary>
        /// 主动断开连接
        /// </summary>
        /// <returns></returns>
        public abstract void Disconnect();

        public abstract Task Send(byte[] buffer, int offset, int count, CancellationToken cancel);
        public abstract Task<int> Receive(byte[] buffer, int offset, CancellationToken cancel);
        public abstract void Dispose();

    }
}
