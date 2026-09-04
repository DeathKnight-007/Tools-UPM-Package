using System;
using System.IO;
using System.Security.Cryptography;

namespace SerializableReadWrite
{
    /// <summary>
    /// 普通文件与加密和/或带校验文件之间的流式转换工具。
    /// </summary>
    public static class FileEncrypt
    {
        private const int FileBufferSize = 1024 * 64;

        /// <summary>
        /// 流式读取原始文件，并生成加密和/或带校验的目标文件。
        /// </summary>
        public static void Encrypt(
            string sourcePath,
            string encryptedPath,
            string passward = null,
            IVerify verify = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            EnsureDifferentPaths(sourcePath, encryptedPath);
            long sourceLength = new FileInfo(sourcePath).Length;

            ProtectedFile.Write(
                encryptedPath,
                outputStream =>
                {
                    using FileStream inputStream = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        FileBufferSize);

                    inputStream.CopyTo(outputStream, FileBufferSize);
                },
                CreateOptions(passward, verify),
                progress,
                sourceLength);
        }

        /// <summary>
        /// 校验并流式解密文件，将明文写入目标文件。
        /// </summary>
        public static void Decrypt(
            string encryptedPath,
            string outputPath,
            string passward = null,
            IVerify verify = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            EnsureDifferentPaths(encryptedPath, outputPath);

            ProtectedFile.Read(
                encryptedPath,
                inputStream =>
                {
                    using FileStream outputStream = new FileStream(
                        outputPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        FileBufferSize);

                    inputStream.CopyTo(outputStream, FileBufferSize);
                    return true;
                },
                CreateOptions(passward, verify),
                progress);
        }

        /// <summary>
        /// 校验并解密文件，将完整明文读取到内存。
        /// </summary>
        public static byte[] DecryptToBytes(
            string encryptedPath,
            string passward = null,
            IVerify verify = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            return ProtectedFile.Read(
                encryptedPath,
                inputStream =>
                {
                    using var memoryStream = new MemoryStream();
                    inputStream.CopyTo(memoryStream, FileBufferSize);
                    return memoryStream.ToArray();
                },
                CreateOptions(passward, verify),
                progress);
        }

        /// <summary>
        /// 使用调用方提供的 AES 流式加密文件，不执行密码密钥派生。
        /// AES 的释放由调用方负责；使用 HMACVerify 时必须提供独立的 verifyKey。
        /// </summary>
        public static void EncryptWithAes(
            string sourcePath,
            string encryptedPath,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            EnsureDifferentPaths(sourcePath, encryptedPath);
            long sourceLength = new FileInfo(sourcePath).Length;

            ProtectedFile.Write(
                encryptedPath,
                outputStream =>
                {
                    using FileStream inputStream = new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        FileBufferSize);

                    inputStream.CopyTo(outputStream, FileBufferSize);
                },
                CreateOptions(aes, verify, verifyKey),
                progress,
                sourceLength);
        }

        /// <summary>
        /// 使用调用方提供的 AES 校验并流式解密文件。
        /// </summary>
        public static void DecryptWithAes(
            string encryptedPath,
            string outputPath,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            EnsureDifferentPaths(encryptedPath, outputPath);

            ProtectedFile.Read(
                encryptedPath,
                inputStream =>
                {
                    using FileStream outputStream = new FileStream(
                        outputPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        FileBufferSize);

                    inputStream.CopyTo(outputStream, FileBufferSize);
                    return true;
                },
                CreateOptions(aes, verify, verifyKey),
                progress);
        }

        /// <summary>
        /// 使用调用方提供的 AES 校验并解密文件到内存。
        /// </summary>
        public static byte[] DecryptToBytesWithAes(
            string encryptedPath,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            return ProtectedFile.Read(
                encryptedPath,
                inputStream =>
                {
                    using var memoryStream = new MemoryStream();
                    inputStream.CopyTo(memoryStream, FileBufferSize);
                    return memoryStream.ToArray();
                },
                CreateOptions(aes, verify, verifyKey),
                progress);
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

        private static void EnsureDifferentPaths(string sourcePath, string outputPath)
        {
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("源文件路径不能为空", nameof(sourcePath));

            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("目标文件路径不能为空", nameof(outputPath));

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullOutputPath = Path.GetFullPath(outputPath);

            if (string.Equals(fullSourcePath, fullOutputPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("源文件路径和目标文件路径不能相同", nameof(outputPath));
            }
        }
    }
}
