using System;
using System.IO;
using System.Security.Cryptography;

namespace SerializableReadWrite
{
    public class HashVerify : IVerify
    {
        /// <summary>
        /// SHA-256 固定输出 256 bit，也就是 32 byte。
        /// </summary>
        public int TagLength => 32;

        public byte[] ComputeTag(byte[] data, byte[] passward = null)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        public byte[] ComputeTag(Stream data, byte[] passward = null)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // ComputeHash 从流的当前位置开始计算到流末尾。
                return sha256.ComputeHash(data);
            }
        }

        public bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null)
        {
            if (tag == null || tag.Length != TagLength)
                return false;

            byte[] calculatedTag = ComputeTag(data);
            return CryptographicOperations.FixedTimeEquals(calculatedTag, tag);
        }

        public bool VerifyTag(Stream data, byte[] tag, byte[] passward = null)
        {
            if (tag == null || tag.Length != TagLength)
                return false;

            byte[] calculatedTag = ComputeTag(data);
            return CryptographicOperations.FixedTimeEquals(calculatedTag, tag);
        }
    }
}
