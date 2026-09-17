using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SerializableReadWrite
{
    public interface ISerializer
    {
        /// <summary>
        /// 将对象序列化到byte数组中
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public int Serialize<T>(T target, byte[] buffer, int offset);
        
        /// <summary>
        /// 直接返回数组
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <returns></returns>
        public byte[] Serialize<T>(T target);

        public string SerializeToString<T>(T target);

        /// <summary>
        /// 序列化到stream里
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="target"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        public void Serialize<T>(T target, Stream stream);

        /// <summary>
        /// 从byte数组中，反序列化出对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public T Deserialize<T>(byte[] buffer, int offset, int count);

        /// <summary>
        /// 从Stream里反序列出出对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="stream"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public T Deserialize<T>(Stream stream);

        /// <summary>
        /// 从字符串中反序列化出对象
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="content"></param>
        /// <returns></returns>
        public T Deserialize<T>(string content);
    }
}
