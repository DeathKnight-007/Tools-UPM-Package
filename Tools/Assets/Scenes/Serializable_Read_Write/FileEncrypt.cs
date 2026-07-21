using System;
using System.IO;

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
            string verifyPassward = null,
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
                CreateOptions(passward, verify, verifyPassward),
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
            string verifyPassward = null,
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
                CreateOptions(passward, verify, verifyPassward),
                progress);
        }

        /// <summary>
        /// 校验并解密文件，将完整明文读取到内存。
        /// </summary>
        public static byte[] DecryptToBytes(
            string encryptedPath,
            string passward = null,
            IVerify verify = null,
            string verifyPassward = null,
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
                CreateOptions(passward, verify, verifyPassward),
                progress);
        }

        private static ProtectedFileOptions CreateOptions(
            string passward,
            IVerify verify,
            string verifyPassward)
        {
            return new ProtectedFileOptions
            {
                EncryptionPassword = passward,
                Verify = verify,
                VerifyKey = verifyPassward
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
