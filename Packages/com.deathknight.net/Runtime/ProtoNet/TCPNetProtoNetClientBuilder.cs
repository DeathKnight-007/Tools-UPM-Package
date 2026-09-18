using SerializableReadWrite;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DeathKnight.Net
{
    public class TCPNetProtoNetClientBuilder
    {
        private ISerializer serializer;
        private int frameBufferSize;
        private NetClient netClient;
        public TCPNetProtoNetClientBuilder()
        {
            serializer = new SerializableReadWrite.JsonSerializer();
            frameBufferSize = 1024 * 64;
            this.netClient = new NetClientBuilder().Build();
        }
        /// <summary>
        /// 设置序列化工,不设置默认是JsonSerializer
        /// </summary>
        /// <param name="serializer"></param>
        /// <returns></returns>
        public TCPNetProtoNetClientBuilder Serializer(ISerializer serializer)
        {
            this.serializer = serializer;
            return this;
        }

        /// <summary>
        /// 设置帧数据最大限制,不设置默认是64KB
        /// </summary>
        /// <param name="frameBufferSize"></param>
        /// <returns></returns>
        public TCPNetProtoNetClientBuilder FrameBufferSize(int frameBufferSize)
        {
            this.frameBufferSize = frameBufferSize;
            return this;
        }

        /// <summary>
        /// 设置网络实体，不设置默认使用默认设置的TCPClient
        /// </summary>
        /// <param name="netClient"></param>
        /// <returns></returns>
        public TCPNetProtoNetClientBuilder NetClient(NetClient netClient)
        {
            this.netClient = netClient;
            return this;
        }
        public TCPNetProtoNetClient Build()
        {
            return new TCPNetProtoNetClient(this.frameBufferSize, this.netClient, this.serializer);
        }
    }
}
