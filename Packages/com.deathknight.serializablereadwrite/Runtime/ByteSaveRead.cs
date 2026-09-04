using System;
using System.IO;
using System.Security.Cryptography;

namespace SerializableReadWrite
{
    /// <summary>
    /// 原始 byte[] 与受保护文件之间的读写适配器。
    /// </summary>
    public static class ByteSaveRead
    {
        public static void Save(
            string path,
            byte[] data,
            string passward = null,
            IVerify verify = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            ProtectedFile.Write(
                path,
                plaintextStream => plaintextStream.Write(data, 0, data.Length),
                CreateOptions(passward, verify));
        }

        public static byte[] Read(
            string path,
            string passward = null,
            IVerify verify = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream =>
                {
                    using var memoryStream = new MemoryStream();
                    plaintextStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                },
                CreateOptions(passward, verify));
        }

        /// <summary>
        /// 使用调用方提供的 AES 保存数据，不执行密码密钥派生。
        /// AES 的释放由调用方负责；使用 HMACVerify 时必须提供独立的 verifyKey。
        /// </summary>
        public static void SaveWithAes(
            string path,
            byte[] data,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            ProtectedFile.Write(
                path,
                plaintextStream => plaintextStream.Write(data, 0, data.Length),
                CreateOptions(aes, verify, verifyKey));
        }

        /// <summary>
        /// 使用调用方提供的 AES 读取数据，不执行密码密钥派生。
        /// </summary>
        public static byte[] ReadWithAes(
            string path,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream =>
                {
                    using var memoryStream = new MemoryStream();
                    plaintextStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                },
                CreateOptions(aes, verify, verifyKey));
        }

        private static ProtectedFileOptions CreateOptions(
            string passward,
            IVerify verify)
        {
            return new ProtectedFileOptions
            {
                EncryptionPassword = passward,
                Verify = verify
            };
        }

        private static ProtectedFileOptions CreateOptions(
            Aes aes,
            IVerify verify,
            byte[] verifyKey)
        {
            if (aes == null)
                throw new ArgumentNullException(nameof(aes));

            return new ProtectedFileOptions
            {
                EncryptionAes = aes,
                Verify = verify,
                VerifyKey = verifyKey
            };
        }
    }
}
