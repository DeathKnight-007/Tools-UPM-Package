using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using UnityEngine;

namespace DeathKnight.Net
{
    public abstract class NetServer : IDisposable
    {
        /// <summary>
        /// 服务器配置
        /// </summary>
        public class ServerConfig
        {
            //public EndPoint endpoint;// 端点基类
            // 重点 AddressFamily，常用端点协议，InterNetwork, InterNetworkV6, Unspecified(ipv1,ipv6都不指定) Bluetooth, 其他基本不用
            public DnsEndPoint dnsEndPoint;// AddressFamily继承，string Host, int Port
                                           //IPAddress[] ips = Dns.GetHostAddresses("www.baidu.com");
                                           // 域名解析过程，是操作系统在做，首先找本机缓存，再次去外网问去（dns域名解析系统，根服务器，com服务器等等分级缓存数据）
                                           // dns服务器，帮忙询问域名与ip的解析。他可以有自己的查询缓存，也可以有自己的权威数据，也可以去别的dns服务器询问
                                           // 局域网中，dns一般是网关，dns服务器一般是路由器，但是电脑也可以作为dns服务器，但是需要安装dns server程序，默认是有dns client程序的
                                           // 自己搭建dns服务器的需求：1、Jenkins不用绑定ip了直接绑定域名，换电脑也不用改ip了  2、机器多需要集中管理
            public List<IPEndPoint> ipEndPoints;// AddressFamily继承, int Port, IPAddress.TryParse("192.168.1.123")
        }
        /// <summary>
        /// 服务器信息，暴漏给外部或者客户端的信息
        /// </summary>
        public class ServerInfo
        {
            //public EndPoint endpoint;// 端点基类
            // 重点 AddressFamily，常用端点协议，InterNetwork, InterNetworkV6, Unspecified(ipv1,ipv6都不指定) Bluetooth, 其他基本不用
            public DnsEndPoint dnsEndPoint;// AddressFamily继承，string Host, int Port
                                           //IPAddress[] ips = Dns.GetHostAddresses("www.baidu.com");
                                           // 域名解析过程，是操作系统在做，首先找本机缓存，再次去外网问去（dns域名解析系统，根服务器，com服务器等等分级缓存数据）
                                           // dns服务器，帮忙询问域名与ip的解析。他可以有自己的查询缓存，也可以有自己的权威数据，也可以去别的dns服务器询问
                                           // 局域网中，dns一般是网关，dns服务器一般是路由器，但是电脑也可以作为dns服务器，但是需要安装dns server程序，默认是有dns client程序的
                                           // 自己搭建dns服务器的需求：1、Jenkins不用绑定ip了直接绑定域名，换电脑也不用改ip了  2、机器多需要集中管理
            public List<IPEndPoint> ipEndPoints;// AddressFamily继承, int Port, IPAddress.TryParse("192.168.1.123")
        }
        public abstract ServerConfig DefaultConfig { get; }
        public abstract ServerConfig Config { get; }
        public abstract ServerInfo Info { get; }
        public abstract void Dispose();
        public abstract bool Init(ServerConfig config);
        public abstract bool StartListener();
        public abstract void OnAcceptConnect();
        public abstract void OnDisconnect();
        public abstract void OnReceiveMessage();
        public abstract void SendMessage();

        public abstract void ShutDown(NetClient.NetClientInfo client);
        public abstract void ShutDownAll();
    }
}
