using System.Collections;
using System.Collections.Generic;
using System;
using SerializableReadWrite;
using System.IO;

namespace DeathKnight.Net
{
    public class ParseBytesObjectVerify<T> : IParseBytesObject<T> where T : VerifyProto, new()
    {
        private Stack<MemoryStream> bigBuffers = new(); // 大块帧数据缓存池
        private Dictionary<long, MemoryStream> bigFrames = new(); // 大块帧数据
        private Dictionary<long, short> bigFramesIndexProgress = new();
        private MemoryStream sendBigFrames;

        public ISerializer Serializer { get; private set; }
        public int FrameBufferSize { get; private set; }
        public ParseBytesObjectVerify(ISerializer Serializer, int FrameBufferSize)
        {
            this.Serializer = Serializer;
            sendBigFrames = new();
            this.FrameBufferSize = FrameBufferSize;
        }
        private T t = new();

        public void ParseHeaderToBytes(T header, byte[] buffer, int offset)
        {
            header.ToBytes(buffer, offset);
        }

        public int ParseObjectToBytes(object value, byte[] buffer, int offset)
        {
            return Serializer.Serialize(value, buffer, offset);
        }

        public void ComputeHash(T header, byte[] buffer, int offset)
        {
            header.ComputeHash(buffer, offset);
        }

        private object currentParseValue;
        public void ParseObjectToBytesFragment(object value, T header)
        {
            currentParseValue = value;
            sendBigFrames.SetLength(0);
            Serializer.Serialize(value, sendBigFrames);
            sendBigFrames.Position = 0;
            header.FragmentInfo.NeedFragment = true;
            header.FragmentInfo.Index = 0;
            double fragmentCount = Math.Ceiling((double)sendBigFrames.Length / (FrameBufferSize - t.HeaderLength));
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
            int writeLength = (int)Math.Min(FrameBufferSize - t.HeaderLength, sendBigFrames.Length - sendBigFrames.Position);
            sendBigFrames.Read(buffer, t.HeaderLength, writeLength);
            header.FragmentInfo.NeedFragment = true;
            header.PayloadLength = writeLength;
            header.FragmentInfo.Index++;
            ParseHeaderToBytes(header, buffer, 0);
            ComputeHash(header, buffer, 0);
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
            return header;
        }
        public object ParseObjectFromBytes(T header, byte[] buffer, int offset, int count)
        {
            if (count < header.PayloadLength)
                throw new ArgumentOutOfRangeException("buffer data is not enugh");
            if (buffer.Length < offset + count)
                throw new ArgumentOutOfRangeException("buffer length is not enugh");
            if (!header.ValidHash(buffer, offset - header.HeaderLength))
            {
                throw new Exception("not valid");
            }
            Type objectType;
            if (!BaseProto.PayloadTypes.TryGetValue(header.MessageType, out objectType))
            {
                throw new Exception("MessageType is not registered");
            }
            return Serializer.Deserialize(objectType, buffer, offset, count);
        }

        private T firstHeader;
        private bool fragmentParsing = false;
        public bool ParseObjectFromBytesFragmentNext(T header, byte[] buffer, int offset, int count)
        {
            try
            {
                if (!header.FragmentInfo.NeedFragment)
                {
                    throw new Exception("fragments header 标识不需要分片");
                }
                if (header.FragmentInfo.TotalCount > header.FragmentMax)
                {
                    throw new ArgumentException("fragment total count 大于 header.FragmentMax");
                }
                if (header.FragmentInfo.Index <= 0 || header.FragmentInfo.Index > header.FragmentInfo.TotalCount)
                {
                    throw new InvalidDataException("Invalid fragment metadata.");
                }
                if (!fragmentParsing)
                {
                    firstHeader = header;
                    fragmentParsing = true;
                }
                else
                {
                    if (header.FragmentInfo.FragmentId != firstHeader.FragmentInfo.FragmentId ||
                        header.FragmentInfo.NeedFragment != firstHeader.FragmentInfo.NeedFragment ||
                         header.FragmentInfo.TotalCount != firstHeader.FragmentInfo.TotalCount ||
                         header.MessageType != firstHeader.MessageType
                        )
                    {
                        throw new Exception("后续来的fragement不符合预期");
                    }
                }
                if (!header.ValidHash(buffer, offset - header.HeaderLength))
                {
                    throw new Exception("not valid");
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
                        mstream.Capacity = FrameBufferSize * 2;
                    }
                    bigFrames.Add(header.FragmentInfo.FragmentId, mstream);
                    bigFramesIndexProgress.Add(header.FragmentInfo.FragmentId, 1);
                }
                else
                {
                    if (header.FragmentInfo.Index != bigFramesIndexProgress[header.FragmentInfo.FragmentId] + 1)
                    {
                        throw new Exception("fragment index error:" + header.FragmentInfo.Index);
                    }
                    bigFramesIndexProgress[header.FragmentInfo.FragmentId]++;
                }
                MemoryStream getstream = bigFrames[header.FragmentInfo.FragmentId];
                getstream.Write(buffer, offset, count);
                if (header.FragmentInfo.Index == header.FragmentInfo.TotalCount)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                if (firstHeader != null)
                {
                    DisposeFragmentStream(firstHeader.FragmentInfo.FragmentId);
                }
                DisposeFragmentStream(header.FragmentInfo.FragmentId);
                throw;
            }
        }

        private void DisposeFragmentStream(long requestid)
        {
            MemoryStream getstream;
            if (bigFrames.TryGetValue(requestid, out getstream))
            {
                if (bigFrames.ContainsKey(requestid))
                {
                    bigFrames.Remove(requestid);
                }
                bigBuffers.Push(getstream);
                getstream.SetLength(0);
                getstream.Position = 0;
            }
            if (bigFramesIndexProgress.ContainsKey(requestid))
            {
                bigFramesIndexProgress.Remove(requestid);
            }
            fragmentParsing = false;
            firstHeader = null;
        }

        public object ParseObjectFromBytesFragmentFinal(T header)
        {
            try
            {
                MemoryStream getstream = bigFrames[header.FragmentInfo.FragmentId];
                Type objectType;
                if (!BaseProto.PayloadTypes.TryGetValue(header.MessageType, out objectType))
                {
                    throw new Exception("MessageType is not registered");
                }
                getstream.Position = 0;
                object Value = Serializer.Deserialize(objectType, getstream);
                if (firstHeader != null)
                {
                    DisposeFragmentStream(firstHeader.FragmentInfo.FragmentId);
                }
                DisposeFragmentStream(header.FragmentInfo.FragmentId);
                return Value;
            }
            catch
            {
                if (firstHeader != null)
                {
                    DisposeFragmentStream(firstHeader.FragmentInfo.FragmentId);
                }
                DisposeFragmentStream(header.FragmentInfo.FragmentId);
                throw;
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
            foreach (var item in bigFrames)
            {
                item.Value.Dispose();
            }
            bigFrames.Clear();
        }

        public T ParseHeaderFromBytes(MemoryStream stream, int count)
        {
            if (count > stream.Length - stream.Position)
            {
                throw new ArgumentException("尝试从不够长度的stream里取超过stream长度的数据");
            }
            if (stream.TryGetBuffer(out var segment))
            {
                T result = ParseHeaderFromBytes(segment.Array, segment.Offset + (int)stream.Position, count);
                return result;
            }
            else
            {
                throw new Exception("不支持 不能获取byte[]的MemoryStream");
            }
        }

        public object ParseObjectFromBytes(T header, MemoryStream stream, int count)
        {
            if (count > stream.Length - stream.Position)
            {
                throw new ArgumentException("尝试从不够长度的stream里取超过stream长度的数据");
            }
            if (stream.TryGetBuffer(out var segment))
            {
                return ParseObjectFromBytes(header, segment.Array, segment.Offset + (int)stream.Position, count);
            }
            else
            {
                throw new Exception("不支持 不能获取byte[]的MemoryStream");
            }
        }

        public bool ParseObjectFromBytesFragmentNext(T header, MemoryStream stream, int count)
        {
            if (count > stream.Length - stream.Position)
            {
                throw new ArgumentException("尝试从不够长度的stream里取超过stream长度的数据");
            }
            if (stream.TryGetBuffer(out var segment))
            {
                return ParseObjectFromBytesFragmentNext(header, segment.Array, segment.Offset + (int)stream.Position, count);
            }
            else
            {
                throw new Exception("不支持 不能获取byte[]的MemoryStream");
            }
        }
    }
}
