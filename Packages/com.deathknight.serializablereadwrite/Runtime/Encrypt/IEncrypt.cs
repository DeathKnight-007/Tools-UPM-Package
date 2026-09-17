using System.IO;

namespace SerializableReadWrite
{
    public interface IEncrypt
    {
        public struct EncryptConfig
        {
            /// <summary>
            /// 用于通过每份数据的随机 Salt 派生 AES Key 和校验 Key 的密码。
            /// </summary>
            public string SimplePassword;

            /// <summary>
            /// 完整性校验算法，不能为空。
            /// </summary>
            public IVerify Verify;
        }

        EncryptConfig Config { get; }

        int Encrypt(
            byte[] contentBuffer,
            int contentOffset,
            int contentCount,
            byte[] encryptBuffer,
            int encryptOffset);

        byte[] Encrypt(byte[] contentBuffer, int contentOffset, int contentCount);
        void Encrypt(Stream contentStream, Stream encryptStream);
        int Encrypt(Stream contentStream, byte[] encryptBuffer, int encryptOffset);
        byte[] Encrypt(Stream contentStream);

        int Decrypt(
            byte[] encryptBuffer,
            int encryptOffset,
            int encryptCount,
            byte[] contentBuffer,
            int contentOffset);

        byte[] Decrypt(byte[] encryptBuffer, int encryptOffset, int encryptCount);
        void Decrypt(Stream encryptStream, Stream contentStream);
        int Decrypt(Stream encryptStream, byte[] contentBuffer, int contentOffset);
        byte[] Decrypt(Stream encryptStream);
    }
}
