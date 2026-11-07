using System.Buffers.Binary;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Collections.ObjectModel;
using System.Text;

namespace DeathKnight.Net
{
    public class BaseProto
    {
        public BaseProto()
        {
            HeaderLength = 38;
        }
        private static Dictionary<int, Type> payloadTypes = new();
        public static readonly ReadOnlyDictionary<int, Type> PayloadTypes = new ReadOnlyDictionary<int, Type>(payloadTypes);

        public static void Register(int messageType, Type type)
        {
            if (PayloadTypes.ContainsKey(messageType))
            {
                if (PayloadTypes[messageType].FullName == type.FullName)
                {
                    return;
                }
                else
                {
                    throw new ArgumentException($"PayloadTypes has contains: messageType:{messageType}, type:{PayloadTypes[messageType].FullName}, and type is {type.FullName}");
                }
            }
            else
            {
                payloadTypes.Add(messageType, type);
            }
        }

        /// <summary>
        /// 一个大消息，最大可以切分成多少帧
        /// </summary>
        public readonly int FragmentMax = 1000;

        public readonly int DefaultMagic = 0x128e597f;

        public readonly byte DefaultVersion = 1;

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
        public int MessageType; // 4
        // 本次请求的id, 从0递增，主要用来判断，服务器是否回复了本次请求
        public long RequestId; // 8
        // 发送方发送时的时间，服务器接收后，复制该值，返给客户端，客户端算出往返时间。这个时间客户端可以自己定义，Unity的Time.time即可。
        public int Timestamp; // 4
        // 用于消息分片,当消息超过协议帧最大数据量
        public FragmentInfo FragmentInfo; // 5 + 8
        public int PayloadLength; // 4
        public short HeaderLength { get; protected set; } // 不写入proto序列化中

        public byte[] ToBytes()
        {
            byte[] result = new byte[HeaderLength];
            ToBytes(result, 0);
            return result;
        }
        public virtual void ToBytes(byte[] buffer, int offset)
        {
            int pos = offset;
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(pos, 4), DefaultMagic);
            pos += 4;
            buffer[pos] = DefaultVersion;
            pos++;
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(pos, 4), MessageType);
            pos += 4;
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(pos, 8), RequestId);
            pos += 8;
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(pos, 4), Timestamp);
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
            BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(pos, 8), FragmentInfo.FragmentId);
            pos += 8;
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(pos, 2), FragmentInfo.Index);
            pos += 2;
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(pos, 2), FragmentInfo.TotalCount);
            pos += 2;
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(pos, 4), PayloadLength);
            pos += 4;
        }
        public virtual void GetProto(byte[] buffer, int offset)
        {
            int pos = offset;
            this.Magic = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos, 4));
            pos += 4;
            this.Version = buffer[pos];
            pos++;
            this.MessageType = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos, 4));
            pos += 4;
            this.RequestId = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(pos, 8));
            pos += 8;
            this.Timestamp = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos, 4));
            pos += 4;
            this.FragmentInfo = new();
            this.FragmentInfo.NeedFragment = buffer[pos] == 1;
            pos++;
            this.FragmentInfo.FragmentId = BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(pos, 8));
            pos += 8;
            this.FragmentInfo.Index = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(pos, 2));
            pos += 2;
            this.FragmentInfo.TotalCount = BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(pos, 2));
            pos += 2;
            this.PayloadLength = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(pos, 4));
            pos += 4;
        }
        public virtual bool Valid()
        {
            if (Magic != DefaultMagic)
            {
                return false;
            }
            if (Version != DefaultVersion)
            {
                return false;
            }
            return true;
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            foreach(var item in this.GetType().GetFields())
            {
                sb.Append(item.Name);
                sb.Append(":");
                sb.Append(item.GetValue(this).ToString());
                sb.Append("\n");
            }
            foreach (var item in this.GetType().GetProperties())
            {
                sb.Append(item.Name);
                sb.Append(":");
                sb.Append(item.GetValue(this).ToString());
                sb.Append("\n");
            }
            sb.Append("}");
            return sb.ToString();
        }
    }

}

