using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SerializableReadWrite;
using System.Buffers.Binary;
using System;
using System.IO;

namespace DeathKnight.Net
{
    public class VerifyProto : BaseProto
    {
        public VerifyProto()
        {
            verify = new HashVerify();
            hash = new byte[32];
            HeaderLength = 38 + 32;
        }
        /// <summary>
        /// 内容摘要
        /// </summary>
        public byte[] hash; // 32
        private HashVerify verify;

        /// <summary>
        /// 计算消息头和消息体的摘要
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        public void ComputeHash(byte[] buffer, int offset)
        {
            hash = verify.ComputeTag(buffer, offset + 32, 38 + PayloadLength);
            Buffer.BlockCopy(hash, 0, buffer, offset, 32);
        }
        public override void ToBytes(byte[] buffer, int offset)
        {
            int pos = offset + 32;
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
        public override void GetProto(byte[] buffer, int offset)
        {
            Buffer.BlockCopy(buffer, offset, hash, 0, 32);
            int pos = offset + 32;
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

        public bool ValidHash(byte[] buffer, int offset)
        {
            if (!verify.VerifyTag(buffer, offset + 32, 38 + PayloadLength, hash))
            {
                return false;
            }
            return true;
        }

        public bool ValidHash(MemoryStream stream)
        {
            if (stream.TryGetBuffer(out var segment))
            {
                return ValidHash(segment.Array, segment.Offset + (int)stream.Position);
            }
            return false;
        }
    }
}
