using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WebSocketSharp;

namespace DeathKnight.Net
{
    /// <summary>
    /// 消息头解析失败，接收方需要主动关闭连接，重新连接
    /// </summary>
    public struct TCPNetHeaderProto
    {
        public int DefaultMagic
        {
            get
            {
                return 0x128e597f;
            }
        }
        public byte DefaultVersion
        {
            get
            {
                return 1;
            }
        }
        /*
         1、承担部分分帧任务
         2、主要承担分帧任务是消息头协议，消息体长度，消息体，这种固定格式分帧
         3、magic可以额外作为分帧的一个检查项，因为tcp已经完全胜任分帧任务了，这个可以检查万一发送的协议不对，相当于一协议的名字
         4、包：没有明确概念，应用层一般指一帧的数据。
        */
        public int Magic;// 4
        // 整个协议的版本，包含消息头的解析协议和消息体的解析协议
        public byte Version; // 1
        /* 
         1、标记消息类型，给网关路由信息
         2、消息类型，同时也给出了数据的解析结构信息
         */
        public ushort MessageType; // 2
        // 本次请求的id, 从0递增，主要用来判断，服务器是否回复了本次请求
        public ulong RequestId; // 8
        // 发送方发送时的时间，服务器接收后，复制该值，返给客户端，客户端算出往返时间。这个时间客户端可以自己定义，Unity的Time.time即可。
        public uint Timestamp; // 4
        // 用于消息分片,当消息超过协议帧最大数据量
        public FragmentInfo FragmentInfo; // 5
        public uint PayloadLength; // 4
        public static ushort HeaderLength // 不写入proto序列化中
        {
            get
            {
                return 28;
            }
        }
        public byte[] ToBytes()
        {
            byte[] result = new byte[HeaderLength];
            ToBytes(result, 0);
            return result;
        }
        public void ToBytes(byte[] buffer, int offset)
        {
            int pos = offset;
            Buffer.BlockCopy(DefaultMagic.ToByteArray(ByteOrder.Big), 0, buffer, pos, 4);
            pos += 4;
            buffer[pos] = DefaultVersion;
            pos++;
            Buffer.BlockCopy(MessageType.ToByteArray(ByteOrder.Big), 0, buffer, pos, 2);
            pos += 2;
            Buffer.BlockCopy(RequestId.ToByteArray(ByteOrder.Big), 0, buffer, pos, 8);
            pos += 8;
            Buffer.BlockCopy(Timestamp.ToByteArray(ByteOrder.Big), 0, buffer, pos, 4);
            pos += 4;
            if (FragmentInfo.NeedFragment)
            {
                buffer[pos] = 1;
            }
            else
            {
                buffer[pos] = 0;
            }
            pos += 1;
            Buffer.BlockCopy(FragmentInfo.FragmentId.ToByteArray(ByteOrder.Big), 0, buffer, pos, 8);
            pos += 8;
            Buffer.BlockCopy(FragmentInfo.Index.ToByteArray(ByteOrder.Big), 0, buffer, pos, 2);
            pos += 2;
            Buffer.BlockCopy(FragmentInfo.TotalCount.ToByteArray(ByteOrder.Big), 0, buffer, pos, 2);
            pos += 2;
            Buffer.BlockCopy(PayloadLength.ToByteArray(ByteOrder.Big), 0, buffer, pos, 4);
            pos += 4;
        }
        public static TCPNetHeaderProto GetProto(byte[] buffer, int offset)
        {
            int pos = offset;
            TCPNetHeaderProto result = new TCPNetHeaderProto();
            result.Magic = BitConverter.ToInt32(buffer, pos);
            pos += 4;
            result.Version = buffer[pos];
            pos++;
            result.MessageType = BitConverter.ToUInt16(buffer, pos);
            pos += 2;
            result.RequestId = BitConverter.ToUInt64(buffer, pos);
            pos += 8;
            result.Timestamp = BitConverter.ToUInt32(buffer, pos);
            pos += 4;
            result.FragmentInfo = new();
            result.FragmentInfo.NeedFragment = buffer[pos] == 1;
            pos ++;
            result.FragmentInfo.FragmentId = BitConverter.ToUInt64(buffer, pos);
            pos += 8;
            result.FragmentInfo.Index = BitConverter.ToUInt16(buffer, pos);
            pos += 2;
            result.FragmentInfo.TotalCount = BitConverter.ToUInt16(buffer, pos);
            pos += 2;
            result.PayloadLength = BitConverter.ToUInt32(buffer, pos);
            pos += 4;
            return result;
        }
        public bool Valid()
        {
            if(Magic != DefaultMagic)
            {
                return false;
            }
            if (Version != DefaultVersion)
            {
                return false;
            }
            return true;
        }
    }
    public struct FragmentInfo
    {
        public bool NeedFragment;//是否需要分片
        public ulong FragmentId; // 这个大片的id，以大片的第一片的RequestId命名
        public ushort Index; // 片序列号，从0开始
        public ushort TotalCount; // 片总数是多少。
    }
}
