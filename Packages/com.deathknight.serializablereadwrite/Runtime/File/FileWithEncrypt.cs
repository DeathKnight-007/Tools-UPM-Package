using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SerializableReadWrite
{
    /// <summary>
    /// 使用指定的加密器读写加密文件，并可配合序列化器直接读写对象。
    /// </summary>
    public class FileWithEncrypt
    {
        private const int FileBufferSize = 1024 * 64;

        private IEncrypt Encrypt { get; }
        private ISerializer Serializer { get; }

        public FileWithEncrypt(IEncrypt encrypt, ISerializer serializer)
        {
            Encrypt = encrypt ?? throw new ArgumentNullException(nameof(encrypt));
            Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>
        /// 读取加密文件，并将解密后的完整内容返回到内存。
        /// </summary>
        public byte[] ReadEncryptFile(string sourcePath)
        {
            ValidateSourcePath(sourcePath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            {
                return Encrypt.Decrypt(sourceStream);
            }
        }

        /// <summary>
        /// 异步读取加密文件，并将解密后的完整内容返回到内存。
        /// </summary>
        public async Task<byte[]> ReadEncryptFileAsync(
            string sourcePath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSourcePath(sourcePath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                true))
            {
                return await Encrypt.DecryptAsync(
                    sourceStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 读取加密文件，并将解密后的内容写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public void ReadEncryptFile(
            string sourcePath,
            string destinationPath)
        {
            EnsureDifferentPaths(sourcePath, destinationPath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            using (var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize))
            {
                Encrypt.Decrypt(sourceStream, destinationStream);
            }
        }

        /// <summary>
        /// 异步读取加密文件，并将解密后的内容写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public async Task ReadEncryptFileAsync(
            string sourcePath,
            string destinationPath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureDifferentPaths(sourcePath, destinationPath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                true))
            using (var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize,
                true))
            {
                await Encrypt.DecryptAsync(
                    sourceStream,
                    destinationStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 读取加密文件，并将解密后的内容反序列化为对象。
        /// </summary>
        public T ReadEncryptFile<T>(string sourcePath)
        {
            ValidateSourcePath(sourcePath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            using (var contentStream = new MemoryStream())
            {
                Encrypt.Decrypt(sourceStream, contentStream);
                contentStream.Position = 0;
                return Serializer.Deserialize<T>(contentStream);
            }
        }

        /// <summary>
        /// 异步读取加密文件，并将解密后的内容反序列化为对象。
        /// </summary>
        public async Task<T> ReadEncryptFileAsync<T>(
            string sourcePath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSourcePath(sourcePath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                true))
            using (var contentStream = new MemoryStream())
            {
                await Encrypt.DecryptAsync(
                    sourceStream,
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                contentStream.Position = 0;
                return await Serializer.DeserializeAsync<T>(
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 加密源文件并写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public void EncryptFile(
            string sourcePath,
            string destinationPath)
        {
            EnsureDifferentPaths(sourcePath, destinationPath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            using (var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                FileBufferSize))
            {
                Encrypt.Encrypt(sourceStream, destinationStream);
            }
        }

        /// <summary>
        /// 异步加密源文件并写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public async Task EncryptFileAsync(
            string sourcePath,
            string destinationPath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            EnsureDifferentPaths(sourcePath, destinationPath);

            using (var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                true))
            using (var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None,
                FileBufferSize,
                true))
            {
                await Encrypt.EncryptAsync(
                    sourceStream,
                    destinationStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 将对象序列化、加密并写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public void EncryptFile<T>(T value, string destinationPath)
        {
            ValidateDestinationPath(destinationPath);

            using (var contentStream = new MemoryStream())
            {
                Serializer.Serialize(value, contentStream);
                contentStream.Position = 0;

                using (var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    FileBufferSize))
                {
                    Encrypt.Encrypt(contentStream, destinationStream);
                }
            }
        }

        /// <summary>
        /// 异步将对象序列化、加密并写入目标文件。
        /// 目标文件已存在时会被覆盖。
        /// </summary>
        public async Task EncryptFileAsync<T>(
            T value,
            string destinationPath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateDestinationPath(destinationPath);

            using (var contentStream = new MemoryStream())
            {
                await Serializer.SerializeAsync(
                    value,
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                contentStream.Position = 0;

                using (var destinationStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    FileBufferSize,
                    true))
                {
                    await Encrypt.EncryptAsync(
                        contentStream,
                        destinationStream,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static void EnsureDifferentPaths(
            string sourcePath,
            string destinationPath)
        {
            ValidateSourcePath(sourcePath);
            ValidateDestinationPath(destinationPath);

            string fullSourcePath = Path.GetFullPath(sourcePath);
            string fullDestinationPath = Path.GetFullPath(destinationPath);

            if (string.Equals(
                fullSourcePath,
                fullDestinationPath,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "源文件路径和目标文件路径不能相同",
                    nameof(destinationPath));
            }
        }

        private static void ValidateSourcePath(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
                throw new ArgumentException("源文件路径不能为空", nameof(sourcePath));
        }

        private static void ValidateDestinationPath(string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                throw new ArgumentException(
                    "目标文件路径不能为空",
                    nameof(destinationPath));
            }
        }
    }
}
