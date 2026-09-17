using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static SerializableReadWrite.IEncrypt;

namespace SerializableReadWrite
{
    /// <summary>
    /// 使用 AES-CBC/PKCS7 加解密数据。
    /// 数据格式为：[Tag][Salt][IV][Ciphertext]。
    /// Tag 的计算范围为 Salt、IV 和密文。
    /// </summary>
    public class AesEncrypt : IEncrypt, IDisposable
    {
        private const int SaltLength = 16;
        private const int IvLength = 16;
        private const int KeyLength = 32;
        private const int Pbkdf2Iterations = 100000;

        private readonly Aes aes;
        private readonly SemaphoreSlim operationLock = new SemaphoreSlim(1, 1);
        private bool disposed;

        public AesEncrypt(EncryptConfig config)
        {
            if (string.IsNullOrEmpty(config.SimplePassword))
            {
                throw new ArgumentException(
                    "SimplePassword 不能为空",
                    nameof(config.SimplePassword));
            }

            if (config.Verify == null)
                throw new ArgumentNullException(nameof(config.Verify));

            if (config.Verify.TagLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(config.Verify),
                    "校验码长度必须大于 0");
            }

            Config = config;
            aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
        }

        public EncryptConfig Config { get; }

        public void Encrypt(Stream contentStream, Stream encryptStream)
        {
            EncryptAsync(contentStream, encryptStream)
                .GetAwaiter()
                .GetResult();
        }

        public async Task EncryptAsync(
            Stream contentStream,
            Stream encryptStream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateEncryptStreams(contentStream, encryptStream);

            await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await EncryptCoreAsync(
                    contentStream,
                    encryptStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                operationLock.Release();
            }
        }

        public byte[] Encrypt(Stream contentStream)
        {
            return EncryptAsync(contentStream)
                .GetAwaiter()
                .GetResult();
        }

        public async Task<byte[]> EncryptAsync(
            Stream contentStream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (contentStream == null)
                throw new ArgumentNullException(nameof(contentStream));

            using (var encryptStream = new MemoryStream())
            {
                await EncryptAsync(
                    contentStream,
                    encryptStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return encryptStream.ToArray();
            }
        }

        public byte[] Encrypt(
            byte[] contentBuffer,
            int contentOffset,
            int contentCount)
        {
            return EncryptAsync(
                contentBuffer,
                contentOffset,
                contentCount).GetAwaiter().GetResult();
        }

        public async Task<byte[]> EncryptAsync(
            byte[] contentBuffer,
            int contentOffset,
            int contentCount,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSegment(
                contentBuffer,
                contentOffset,
                contentCount,
                nameof(contentBuffer));

            using (var contentStream = new MemoryStream(
                contentBuffer,
                contentOffset,
                contentCount,
                writable: false))
            {
                return await EncryptAsync(
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        [Obsolete("请使用 byte[] Encrypt(Stream contentStream)")]
        public int Encrypt(
            Stream contentStream,
            byte[] encryptBuffer,
            int encryptOffset)
        {
            ValidateDestination(
                encryptBuffer,
                encryptOffset,
                nameof(encryptBuffer));

            byte[] result = Encrypt(contentStream);
            EnsureDestinationCapacity(
                encryptBuffer,
                encryptOffset,
                result.Length,
                nameof(encryptBuffer));
            Buffer.BlockCopy(result, 0, encryptBuffer, encryptOffset, result.Length);
            return result.Length;
        }

        [Obsolete("请使用 byte[] Encrypt(byte[] contentBuffer, int contentOffset, int contentCount)")]
        public int Encrypt(
            byte[] contentBuffer,
            int contentOffset,
            int contentCount,
            byte[] encryptBuffer,
            int encryptOffset)
        {
            ValidateDestination(
                encryptBuffer,
                encryptOffset,
                nameof(encryptBuffer));

            byte[] result = Encrypt(contentBuffer, contentOffset, contentCount);
            EnsureDestinationCapacity(
                encryptBuffer,
                encryptOffset,
                result.Length,
                nameof(encryptBuffer));
            Buffer.BlockCopy(result, 0, encryptBuffer, encryptOffset, result.Length);
            return result.Length;
        }

        public void Decrypt(Stream encryptStream, Stream contentStream)
        {
            DecryptAsync(encryptStream, contentStream)
                .GetAwaiter()
                .GetResult();
        }

        public async Task DecryptAsync(
            Stream encryptStream,
            Stream contentStream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateDecryptStreams(encryptStream, contentStream);

            await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                await DecryptCoreAsync(
                    encryptStream,
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                operationLock.Release();
            }
        }

        public byte[] Decrypt(Stream encryptStream)
        {
            return DecryptAsync(encryptStream)
                .GetAwaiter()
                .GetResult();
        }

        public async Task<byte[]> DecryptAsync(
            Stream encryptStream,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (encryptStream == null)
                throw new ArgumentNullException(nameof(encryptStream));

            using (var contentStream = new MemoryStream())
            {
                await DecryptAsync(
                    encryptStream,
                    contentStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return contentStream.ToArray();
            }
        }

        public byte[] Decrypt(
            byte[] encryptBuffer,
            int encryptOffset,
            int encryptCount)
        {
            return DecryptAsync(
                encryptBuffer,
                encryptOffset,
                encryptCount).GetAwaiter().GetResult();
        }

        public async Task<byte[]> DecryptAsync(
            byte[] encryptBuffer,
            int encryptOffset,
            int encryptCount,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidateSegment(
                encryptBuffer,
                encryptOffset,
                encryptCount,
                nameof(encryptBuffer));

            using (var encryptStream = new MemoryStream(
                encryptBuffer,
                encryptOffset,
                encryptCount,
                writable: false))
            {
                return await DecryptAsync(
                    encryptStream,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        public int Decrypt(
            Stream encryptStream,
            byte[] contentBuffer,
            int contentOffset)
        {
            ValidateDestination(
                contentBuffer,
                contentOffset,
                nameof(contentBuffer));

            byte[] result = Decrypt(encryptStream);
            EnsureDestinationCapacity(
                contentBuffer,
                contentOffset,
                result.Length,
                nameof(contentBuffer));
            Buffer.BlockCopy(result, 0, contentBuffer, contentOffset, result.Length);
            return result.Length;
        }

        public int Decrypt(
            byte[] encryptBuffer,
            int encryptOffset,
            int encryptCount,
            byte[] contentBuffer,
            int contentOffset)
        {
            ValidateDestination(
                contentBuffer,
                contentOffset,
                nameof(contentBuffer));

            byte[] result = Decrypt(encryptBuffer, encryptOffset, encryptCount);
            EnsureDestinationCapacity(
                contentBuffer,
                contentOffset,
                result.Length,
                nameof(contentBuffer));
            Buffer.BlockCopy(result, 0, contentBuffer, contentOffset, result.Length);
            return result.Length;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            aes.Dispose();
            operationLock.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task EncryptCoreAsync(
            Stream contentStream,
            Stream encryptStream,
            IProgress<ReadWriteProgress> progress,
            CancellationToken cancellationToken)
        {
            int tagLength = Config.Verify.TagLength;
            long recordStart = encryptStream.Position;
            await encryptStream.WriteAsync(
                new byte[tagLength],
                0,
                tagLength,
                cancellationToken).ConfigureAwait(false);
            long authenticatedDataStart = encryptStream.Position;

            byte[] salt = GenerateRandomBytes(SaltLength);
            byte[] encryptionKey = null;
            byte[] verificationKey = null;

            try
            {
                DerivedKeys keys = await DeriveKeysAsync(
                    Config.SimplePassword,
                    salt,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                encryptionKey = keys.EncryptionKey;
                verificationKey = keys.VerificationKey;

                await encryptStream.WriteAsync(
                    salt,
                    0,
                    salt.Length,
                    cancellationToken).ConfigureAwait(false);

                aes.Key = encryptionKey;
                aes.GenerateIV();
                byte[] iv = aes.IV;

                await encryptStream.WriteAsync(
                    iv,
                    0,
                    iv.Length,
                    cancellationToken).ConfigureAwait(false);

                long plaintextLength = AsyncStreamProgress.GetRemainingLength(contentStream);
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                using (CryptoStream cryptoStream = new CryptoStream(
                    encryptStream,
                    encryptor,
                    CryptoStreamMode.Write,
                    leaveOpen: true))
                {
                    await AsyncStreamProgress.CopyToAsync(
                        contentStream,
                        cryptoStream,
                        ReadWriteStage.Encrypting,
                        progress,
                        cancellationToken,
                        plaintextLength).ConfigureAwait(false);
                    cryptoStream.FlushFinalBlock();
                }

                await encryptStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                long recordEnd = encryptStream.Position;
                encryptStream.Position = authenticatedDataStart;

                byte[] tag = await Config.Verify.ComputeTagAsync(
                    encryptStream,
                    Config.Verify.NeedPassword ? verificationKey : null,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (tag == null || tag.Length != tagLength)
                {
                    throw new InvalidDataException(
                        "校验算法返回的 Tag 长度与 TagLength 不一致");
                }

                encryptStream.Position = recordStart;
                await encryptStream.WriteAsync(
                    tag,
                    0,
                    tag.Length,
                    cancellationToken).ConfigureAwait(false);
                encryptStream.Position = recordEnd;
            }
            finally
            {
                ClearKey(encryptionKey);
                ClearKey(verificationKey);
            }
        }

        private async Task DecryptCoreAsync(
            Stream encryptStream,
            Stream contentStream,
            IProgress<ReadWriteProgress> progress,
            CancellationToken cancellationToken)
        {
            byte[] tag = new byte[Config.Verify.TagLength];
            await ReadExactlyAsync(
                encryptStream,
                tag,
                0,
                tag.Length,
                cancellationToken).ConfigureAwait(false);
            long authenticatedDataStart = encryptStream.Position;

            byte[] salt = new byte[SaltLength];
            byte[] iv = new byte[IvLength];
            byte[] encryptionKey = null;
            byte[] verificationKey = null;

            try
            {
                await ReadExactlyAsync(
                    encryptStream,
                    salt,
                    0,
                    salt.Length,
                    cancellationToken).ConfigureAwait(false);

                DerivedKeys keys = await DeriveKeysAsync(
                    Config.SimplePassword,
                    salt,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                encryptionKey = keys.EncryptionKey;
                verificationKey = keys.VerificationKey;

                await ReadExactlyAsync(
                    encryptStream,
                    iv,
                    0,
                    iv.Length,
                    cancellationToken).ConfigureAwait(false);

                long ciphertextStart = encryptStream.Position;
                long ciphertextLength = encryptStream.Length - ciphertextStart;
                if (ciphertextLength <= 0 || ciphertextLength % IvLength != 0)
                {
                    throw new InvalidDataException(
                        "AES 密文长度必须是非零的 16 字节倍数");
                }

                encryptStream.Position = authenticatedDataStart;
                bool valid = await Config.Verify.VerifyTagAsync(
                    encryptStream,
                    tag,
                    Config.Verify.NeedPassword ? verificationKey : null,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                if (!valid)
                    throw new InvalidDataException("加密数据完整性校验不通过");

                encryptStream.Position = ciphertextStart;
                aes.Key = encryptionKey;
                aes.IV = iv;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (CryptoStream cryptoStream = new CryptoStream(
                    encryptStream,
                    decryptor,
                    CryptoStreamMode.Read,
                    leaveOpen: true))
                {
                    await AsyncStreamProgress.CopyToAsync(
                        cryptoStream,
                        contentStream,
                        ReadWriteStage.Decrypting,
                        progress,
                        cancellationToken,
                        ciphertextLength).ConfigureAwait(false);
                }
            }
            finally
            {
                ClearKey(encryptionKey);
                ClearKey(verificationKey);
            }
        }

        private static async Task<DerivedKeys> DeriveKeysAsync(
            string password,
            byte[] salt,
            IProgress<ReadWriteProgress> progress,
            CancellationToken cancellationToken)
        {
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.DerivingKey,
                0,
                1);

            DerivedKeys keys = await Task.Run(
                () => DeriveKeys(password, salt),
                cancellationToken).ConfigureAwait(false);

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.DerivingKey,
                1,
                1);
            return keys;
        }

        private static DerivedKeys DeriveKeys(string password, byte[] salt)
        {
            using (var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256))
            {
                byte[] keyMaterial = derive.GetBytes(KeyLength * 2);
                try
                {
                    var encryptionKey = new byte[KeyLength];
                    var verificationKey = new byte[KeyLength];
                    Buffer.BlockCopy(
                        keyMaterial,
                        0,
                        encryptionKey,
                        0,
                        KeyLength);
                    Buffer.BlockCopy(
                        keyMaterial,
                        KeyLength,
                        verificationKey,
                        0,
                        KeyLength);
                    return new DerivedKeys(encryptionKey, verificationKey);
                }
                finally
                {
                    ClearKey(keyMaterial);
                }
            }
        }

        private static byte[] GenerateRandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            return bytes;
        }

        private static async Task ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            while (count > 0)
            {
                int readCount = await stream.ReadAsync(
                    buffer,
                    offset,
                    count,
                    cancellationToken).ConfigureAwait(false);

                if (readCount == 0)
                    throw new EndOfStreamException("加密数据提前结束");

                offset += readCount;
                count -= readCount;
            }
        }

        private static void ValidateEncryptStreams(
            Stream contentStream,
            Stream encryptStream)
        {
            if (contentStream == null)
                throw new ArgumentNullException(nameof(contentStream));
            if (encryptStream == null)
                throw new ArgumentNullException(nameof(encryptStream));
            if (!contentStream.CanRead)
                throw new ArgumentException("明文输入流必须可读", nameof(contentStream));
            if (!encryptStream.CanWrite || !encryptStream.CanRead || !encryptStream.CanSeek)
            {
                throw new ArgumentException(
                    "加密输出流必须可读、可写且可定位",
                    nameof(encryptStream));
            }
            if (ReferenceEquals(contentStream, encryptStream))
                throw new ArgumentException("输入流和输出流不能是同一个实例");
        }

        private static void ValidateDecryptStreams(
            Stream encryptStream,
            Stream contentStream)
        {
            if (encryptStream == null)
                throw new ArgumentNullException(nameof(encryptStream));
            if (contentStream == null)
                throw new ArgumentNullException(nameof(contentStream));
            if (!encryptStream.CanRead || !encryptStream.CanSeek)
                throw new ArgumentException("加密输入流必须可读且可定位", nameof(encryptStream));
            if (!contentStream.CanWrite)
                throw new ArgumentException("明文输出流必须可写", nameof(contentStream));
            if (ReferenceEquals(encryptStream, contentStream))
                throw new ArgumentException("输入流和输出流不能是同一个实例");
        }

        private static void ValidateSegment(
            byte[] buffer,
            int offset,
            int count,
            string paramName)
        {
            if (buffer == null)
                throw new ArgumentNullException(paramName);
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));
        }

        private static void ValidateDestination(
            byte[] buffer,
            int offset,
            string paramName)
        {
            if (buffer == null)
                throw new ArgumentNullException(paramName);
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
        }

        private static void EnsureDestinationCapacity(
            byte[] buffer,
            int offset,
            int requiredCount,
            string paramName)
        {
            if (requiredCount > buffer.Length - offset)
            {
                throw new ArgumentException(
                    "目标缓冲区剩余空间不足",
                    paramName);
            }
        }

        private static void ClearKey(byte[] key)
        {
            if (key != null)
                Array.Clear(key, 0, key.Length);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AesEncrypt));
        }

        private readonly struct DerivedKeys
        {
            public DerivedKeys(byte[] encryptionKey, byte[] verificationKey)
            {
                EncryptionKey = encryptionKey;
                VerificationKey = verificationKey;
            }

            public byte[] EncryptionKey { get; }
            public byte[] VerificationKey { get; }
        }
    }
}
