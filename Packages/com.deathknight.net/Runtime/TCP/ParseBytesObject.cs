using System.Collections;
using System.Collections.Generic;
using System;
using SerializableReadWrite;
using System.IO;

namespace DeathKnight.Net
{
    public class ParseBytesObject<T> : IDisposable where T : BaseProto, new()
    {
        private Stack<MemoryStream> bigBuffers = new(); // 大块帧数据缓存池
        private Dictionary<long, MemoryStream> bigFrames = new(); // 大块帧数据
        private Dictionary<long, short> bigFramesIndexProgress = new();
        private MemoryStream sendBigFrames;

        ISerializer serializer;
        int frameBufferSize;
        public ParseBytesObject(ISerializer serializer, int FrameBufferSize)
        {
            this.serializer = serializer;
            sendBigFrames = new();
            frameBufferSize = FrameBufferSize;
        }
        private T t = new();

        public void ParseHeaderToBytes(T header, byte[] buffer, int offset)
        {
            header.ToBytes(buffer, offset);
        }

        public int ParseObjectToBytes(object value, byte[] buffer, int offset)
        {
            return serializer.Serialize(value, buffer, offset);
        }

        private object currentParseValue;
        public void ParseObjectToBytesFragment(object value, T header)
        {
            currentParseValue = value;
            sendBigFrames.SetLength(0);
            serializer.Serialize(value, sendBigFrames);
            sendBigFrames.Position = 0;
            header.FragmentInfo.NeedFragment = true;
            header.FragmentInfo.Index = 0;
            double fragmentCount = Math.Ceiling((double)sendBigFrames.Length / (frameBufferSize - t.HeaderLength));
            if (fragmentCount > t.FragmentMax)
            {
                throw new ArgumentOutOfRangeException("send data too long can not slice");
            }
            header.FragmentInfo.TotalCount = (short)Math.Max(fragmentCount, 1);
        }

        public bool ParseObjectToBytesFragmentNext(object value, T header, byte[] buffer)
        {
            if (currentParseValue != value)
            {
                throw new InvalidOperationException("need parse value first");
            }
            int writeLength = (int)Math.Min(frameBufferSize - t.HeaderLength, sendBigFrames.Length - sendBigFrames.Position);
            sendBigFrames.Read(buffer, t.HeaderLength, writeLength);
            header.FragmentInfo.NeedFragment = true;
            header.PayloadLength = writeLength;
            header.FragmentInfo.Index++;
            ParseHeaderToBytes(header, buffer, 0);
            if (sendBigFrames.Position >= sendBigFrames.Length)
            {
                sendBigFrames.SetLength(0);
                return false;
            }
            else
            {
                return true;
            }
        }

        public T ParseHeaderFromBytes(byte[] buffer, int offset, int count)
        {
            if (count < t.HeaderLength)
                throw new ArgumentOutOfRangeException("buffer data is not enugh");
            if (buffer.Length < offset + count)
                throw new ArgumentOutOfRangeException("buffer length is not enugh");
            T header = new();
            header.GetProto(buffer, offset);
            if (!header.Valid())
            {
                throw new Exception("header is not valid");
            }
            return header;
        }
        public object ParseObjectFromBytes(T header, byte[] buffer, int offset, int count)
        {
            if (count < header.PayloadLength)
                throw new ArgumentOutOfRangeException("buffer data is not enugh");
            if (buffer.Length < offset + count)
                throw new ArgumentOutOfRangeException("buffer length is not enugh");
            Type objectType;
            if(!BaseProto.PayloadTypes.TryGetValue(header.MessageType, out objectType))
            {
                throw new Exception("MessageType is not registered");
            }
            return serializer.Deserialize(objectType, buffer, offset, count);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="header"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="value"></param>
        /// <returns>是否完成了切片合并转化</returns>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="Exception"></exception>
        public bool ParseObjectFromBytesFragment(T header, byte[] buffer, int offset, int count, out object value)
        {
            if (header.FragmentInfo.Index <= 0 || header.FragmentInfo.Index > header.FragmentInfo.TotalCount)
            {
                throw new InvalidDataException("Invalid fragment metadata.");
            }
            if (!bigFrames.ContainsKey(header.FragmentInfo.FragmentId))
            {
                // 切片必须依序到达
                if (header.FragmentInfo.Index != 1)
                {
                    throw new Exception("fragment index error 1");
                }
                MemoryStream mstream;
                if (!bigBuffers.TryPop(out mstream))
                {
                    mstream = new MemoryStream();
                    mstream.Capacity = frameBufferSize * 2;
                }
                bigFrames.Add(header.FragmentInfo.FragmentId, mstream);
                bigFramesIndexProgress.Add(header.FragmentInfo.FragmentId, 1);
            }
            else
            {
                if(header.FragmentInfo.Index != bigFramesIndexProgress[header.FragmentInfo.FragmentId] + 1)
                {
                    throw new Exception("fragment index error:" + header.FragmentInfo.Index);
                }
                bigFramesIndexProgress[header.FragmentInfo.FragmentId]++;
            }
            MemoryStream getstream = bigFrames[header.FragmentInfo.FragmentId];
            getstream.Write(buffer, offset, count);
            if (header.FragmentInfo.Index == header.FragmentInfo.TotalCount)
            {
                Type objectType;
                if (!BaseProto.PayloadTypes.TryGetValue(header.MessageType, out objectType))
                {
                    throw new Exception("MessageType is not registered");
                }
                getstream.Position = 0;
                object Value = serializer.Deserialize(objectType, getstream);
                bigFrames.Remove(header.FragmentInfo.FragmentId);
                bigFramesIndexProgress.Remove(header.FragmentInfo.FragmentId);
                getstream.SetLength(0);
                bigBuffers.Push(getstream);
                value = Value;
                return true;
            }
            else
            {
                value = null;
                return false;
            }
        }
        public void Dispose()
        {
            sendBigFrames.Dispose();
            MemoryStream ms;
            while (bigBuffers.TryPop(out ms))
            {
                ms.Dispose();
            }
            bigBuffers.Clear();
            foreach(var item in bigFrames)
            {
                item.Value.Dispose();
            }
            bigFrames.Clear();
        }
    }
}
