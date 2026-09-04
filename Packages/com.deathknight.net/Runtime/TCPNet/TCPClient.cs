using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public class TCPClient : NetClient
    {
        public TCPClient()
        {
            defaultConfig = new NetClientConfig();
            defaultConfig.WriteTimeout = 10 * 1000; // 10s
            defaultConfig.OSSendBufferSize = 0; // 使用默认
            defaultConfig.ReceiveTimeout = 0; // 永久等待数据
            defaultConfig.OSReceiveBufferSize = 0; // 使用默认
        }
        private NetClientConfig config;
        public override NetClientConfig Config
        {
            get
            {
                return config;
            }
        }

        private NetClientInfo info;
        public override NetClientInfo Info
        {
            get
            {
                return info;
            }
        }

        private NetClientConfig defaultConfig;
        public override NetClientConfig DefaultConfig
        {
            get
            {
                return defaultConfig;
            }
        }

        private NetSimpleClient simpleClient;
        public override NetSimpleClient SimpleClient
        {
            get
            {
                return simpleClient;
            }
        }

        /// <summary>
        /// 异步连接远端， 异常类型有SocketException和Exception
        /// </summary>
        /// <param name="dnsEndPoint"></param>
        /// <returns></returns>
        public override async Task Connect(DnsEndPoint dnsEndPoint)
        {
            IPAddress[] ipas = await Dns.GetHostAddressesAsync(dnsEndPoint.Host);
            IPEndPoint[] ips = new IPEndPoint[ipas.Length];
            for (int i = 0; i < ipas.Length; i++)
            {
                ips[i] = new IPEndPoint(ipas[i], dnsEndPoint.Port);
            }
            await Connect(ips);
        }

        public override void Dispose()
        {
            simpleClient?.Dispose();
        }

        public override void Init(NetClientConfig config)
        {
            this.config = config;
        }

        public override async Task Connect(IPEndPoint ipEndPoint)
        {
            simpleClient?.Disconnect();
            simpleClient?.Dispose();
            simpleClient = new TCPSimpleClient();
            simpleClient.Init(config);
            await simpleClient.Connect(ipEndPoint);
        }

        public override async Task Connect(IPEndPoint[] ipEndPoints)
        {
            simpleClient?.Disconnect();
            simpleClient?.Dispose();
            // 使用策略
            switch (config.ipsTryConnectStrategy)
            {
                case IPsTryConnectStrategy.UseFirst:
                    await Connect(ipEndPoints[0]);
                    break;
                case IPsTryConnectStrategy.HappyEyeballs:
                    HappyEyeballsSelectStrategy<TCPSimpleClient> strategy = new();
                    simpleClient = await strategy.Select(ipEndPoints, config);
                    break;
            }
        }

        public override void Disconnect()
        {
            simpleClient?.Disconnect();
        }

        public override async Task Send(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            if (simpleClient == null)
            {
                throw new InvalidOperationException("simpleClient is not initialized or has been disconnected.");
            }
            await simpleClient.Send(buffer, offset, count, cancel);
        }

        public override async Task<int> Receive(byte[] buffer, int offset, CancellationToken cancel)
        {
            if (simpleClient == null)
            {
                throw new InvalidOperationException("simpleClient is not initialized or has been disconnected.");
            }
            return await simpleClient.Receive(buffer, offset, cancel);
        }
    }
}
