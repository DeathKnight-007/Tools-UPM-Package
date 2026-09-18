using ProtoBuf.Meta;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using UnityEngine;
using static DeathKnight.Net.NetClient;

namespace DeathKnight.Net
{
    public class NetClientBuilder
    {
        private NetClient.NetClientConfig config;
        private AvaliableNetType netType;
        public NetClientBuilder()
        {
            this.netType = AvaliableNetType.TCP;

            config.encryptInfo = new();
            config.encryptInfo.EncryptStrategy = NetClient.EncryptStrategy.TLS;
            config.encryptInfo.X509CertificatesLoader = null;
            config.ipsTryConnectStrategy = NetClient.IPsTryConnectStrategy.HappyEyeballs;
            config.ReceiveTimeout = 0; // 永久等待数据
            config.WriteTimeout = 0; // 永久等待写入数据
            config.AdvanceConfig.OSSendBufferSize = 0;// 默认系统自己选择最优
            config.AdvanceConfig.OSReceiveBufferSize = 0;// 默认系统自己选择最优
        }

        /// <summary>
        /// 设置网络类型,默认使用TCPClient
        /// </summary>
        /// <param name="netType"></param>
        /// <returns></returns>
        public NetClientBuilder NetType(AvaliableNetType netType)
        {
            this.netType = netType;
            return this;
        }

        /// <summary>
        /// 设置加密策略，默认使用标准TLS
        /// </summary>
        /// <param name="netType"></param>
        /// <returns></returns>
        public NetClientBuilder EncryptStrategy(NetClient.EncryptStrategy strategy)
        {
            config.encryptInfo.EncryptStrategy = strategy;
            return this;
        }

        /// <summary>
        /// 设置受信任根证书加载方式，如果是使用私有证书，需要设置这个加载方式
        /// </summary>
        /// <returns></returns>
        public NetClientBuilder CertificatesLoader(Func<X509Certificate2Collection> X509CertificatesLoader)
        {
            config.encryptInfo.X509CertificatesLoader = X509CertificatesLoader;
            return this;
        }

        /// <summary>
        /// 设置IP池尝试连接策略，默认使用happy eyeballs策略，ipv4和ipv6交错尝试连接，谁先连接上连接谁
        /// </summary>
        /// <param name="strategy"></param>
        /// <returns></returns>
        public NetClientBuilder IPsTryConnectStrategy(IPsTryConnectStrategy strategy)
        {
            config.ipsTryConnectStrategy = strategy;
            return this;
        }

        /// <summary>
        /// 接收超时，
        /// </summary>
        /// <param name="receiveTimeout"></param>
        /// <returns></returns>
        public NetClientBuilder ReceiveTimeout(int receiveTimeout)
        {
            config.ReceiveTimeout = receiveTimeout;
            return this;
        }

        public NetClientBuilder SendTimeout(int sendTimeout)
        {
            config.WriteTimeout = sendTimeout;
            return this;
        }

        public NetClient Build()
        {
            NetClient client;
            switch (netType)
            {
                case AvaliableNetType.TCP:
                    client = new TCPClient();
                    client.Init(config);
                    break;
                default:
                    client = new TCPClient();
                    client.Init(config);
                    break;
            }
            return client;
        }
    }
}
