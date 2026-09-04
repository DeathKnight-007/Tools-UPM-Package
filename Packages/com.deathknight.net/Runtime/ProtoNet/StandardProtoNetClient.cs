using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DeathKnight.Net
{
    public class StandardProtoNetClient : IDisposable
    {
        public readonly struct ProtoNetClientConfig
        {
            public ProtoNetClientConfig(int FrameBufferSize, int ReceiveBufferSize)
            {
                this.FrameBufferSize = FrameBufferSize;
            }
            public int FrameBufferSize { get; }
        }
        public ProtoNetClientConfig DefaultConfig { get; }

        public ProtoNetClientConfig Config{ get; }

        private NetClient netClient;

        private byte[] readBuffer;

        private StandardNetProto StandardNetProto;

        private CancellationTokenSource cancelTokenSource;

        /// <summary>
        /// 默认使用TCPClient
        /// </summary>
        /// <param name="config"></param>
        public StandardProtoNetClient(ProtoNetClientConfig config)
        {
            this.netClient = new TCPClient();
            netClient.Init(netClient.DefaultConfig);
            Config = config;
            readBuffer = new byte[config.FrameBufferSize];
            StandardNetProto = new();
            cancelTokenSource = new CancellationTokenSource();
        }
        public StandardProtoNetClient(ProtoNetClientConfig config, NetClient netClient)
        {
            this.netClient = netClient;
            Config = config;
            readBuffer = new byte[config.FrameBufferSize];
            StandardNetProto = new();
            cancelTokenSource = new CancellationTokenSource();
        }
        public void Dispose()
        {
            cancelTokenSource.Dispose();
            this.netClient?.Dispose();
        }

        private ConcurrentQueue<(byte[],int, int)> payloadQueue = new();
        /// <summary>
        /// 发送数据数据
        /// </summary>
        /// <param name="buffer">明文数据</param>
        /// <param name="offset">明文数据起始位置，要预留下消息头位置，本协议offset应该是16字节</param>
        /// <param name="count"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Send(byte[] buffer, int offset, int count)
        {
            if(offset != StandardNetProto.HeaderLength)
            {
                throw new ArgumentException("offset 必须是本协议的消息头长度：" + StandardNetProto.HeaderLength);
            }
            if(count + StandardNetProto.HeaderLength > Config.FrameBufferSize)
            {
                throw new ArgumentOutOfRangeException("payload + header is out of FrameBufferSize");
            }
            payloadQueue.Enqueue((buffer, offset, count));
            Task.Run(CheckAddSend);
        }
        private async Task CheckAddSend()
        {
            if (payloadQueue.Count <= 0)
            {
                return;
            }
            if (netClient == null)
            {
                throw new ArgumentNullException("net client not inited or inited fail");
            }
            while (payloadQueue.TryDequeue(out var item))
            {
                await netClient.Send(
                    item.Item1,
                    0,
                    item.Item3 + item.Item2,
                    cancelTokenSource.Token);
            }
        }
        
    }
}
