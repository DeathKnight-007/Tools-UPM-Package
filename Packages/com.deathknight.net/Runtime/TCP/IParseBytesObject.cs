using SerializableReadWrite;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DeathKnight.Net
{
    public interface IParseBytesObject<T> : IDisposable where T : BaseProto, new()
    {
        ISerializer Serializer { get; }

        int FrameBufferSize { get; }

        void ParseHeaderToBytes(T header, byte[] buffer, int offset);

        int ParseObjectToBytes(object value, byte[] buffer, int offset);

        void ParseObjectToBytesFragment(object value, T header);

        bool ParseObjectToBytesFragmentNext(object value, T header, byte[] buffer);

        T ParseHeaderFromBytes(byte[] buffer, int offset, int count);

        object ParseObjectFromBytes(T header, byte[] buffer, int offset, int count);

        /// <summary>
        /// 将object从多个片段里解析出来，只有所有片段依序到达，才会out出value
        /// </summary>
        /// <param name="header"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <param name="value"></param>
        /// <returns>是否完成了切片合并转化</returns>
        /// <exception cref="InvalidDataException"></exception>
        /// <exception cref="Exception"></exception>
        bool ParseObjectFromBytesFragment(T header, byte[] buffer, int offset, int count, out object value);

        T ParseHeaderFromBytes(MemoryStream stream, int count);

        object ParseObjectFromBytes(T header, MemoryStream stream, int count);

        bool ParseObjectFromBytesFragment(T header, MemoryStream stream, int count, out object value);
    }
}
