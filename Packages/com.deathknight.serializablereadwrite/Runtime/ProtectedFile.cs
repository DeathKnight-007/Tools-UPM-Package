using System;
using System.IO;
using System.Security.Cryptography;

namespace SerializableReadWrite
{
    /// <summary>
    /// 负责明文字节流与受保护文件之间的转换。
    /// 不关心上层写入的是 JSON、图片还是其他二进制数据。
    /// 密码模式的文件布局为：[Tag][salt][IV][data]；直接 AES 模式没有 salt。
    /// 密码模式通过 PBKDF2 派生独立的 AES Key 和 HMAC Key；直接 AES 模式跳过派生。
    /// 校验范围包含已写入的 salt、IV 和 data；启用 AES 时，data 是密文。
    /// </summary>
    public static class ProtectedFile
    {
        private const int SaltLength = 16;
        private const int IvLength = 16;
        private const int FileBufferSize = 1024 * 64;

        /// <summary>
        /// 把上层产生的明文字节流写入受保护文件。
        /// </summary>
        public static void Write(
            string path,
            Action<Stream> writePlaintext,
            ProtectedFileOptions options = null,
            IProgress<FileEncryptProgress> progress = null,
            long plaintextLength = -1)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (writePlaintext == null)
                throw new ArgumentNullException(nameof(writePlaintext));

            options ??= new ProtectedFileOptions();
            ValidateOptions(options);

            if (progress != null && plaintextLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(plaintextLength),
                    "启用进度时必须提供明文总长度");
            }

            FileEncryptProgressReporter progressReporter = progress == null
                ? null
                : new FileEncryptProgressReporter(
                    progress,
                    options.Verify == null ? 1 : 2);

            Aes aes = options.EncryptionAes;
            bool disposeAes = false;
            byte[] salt = null;
            byte[] verifyKey = CloneKey(options.VerifyKey);

            if (options.EncryptionPassword != null)
            {
                aes = CreatePasswordAes(
                    options.EncryptionPassword,
                    out salt,
                    out verifyKey);
                disposeAes = true;
            }

            try
            {
                using (FileStream fs = new FileStream(
                    path,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    FileBufferSize,
                    false))
                {
                    // fs.SetLength(100); 提前设置文件大小，好让操作系统和硬盘分配空间，致使文件不碎片化严重

                    // fs.Position = 0; fs.Seek(0, SeekOrigin.Begin); 效果一样都是移动文件指针，read和write时，指针自动移动

                    // FileMode.OpenOrCreate 设置读写模式
                    // FileMode.OpenOrCreate、FileMode.Open 都是打开文件，然后文件指针设置在文件开头，但是保留了原本文件内容
                    // FileMode.Create 打开文件，清空原有文本内容，并且文件指针在开头
                    // FileMode.CreateNew 一般用不到，如果文件存在，就会报错，它必须是创建新文件

                    // FileAccess.ReadWrite、FileShare.None 设置获取资源权限，后者表示不把权限分享出去，前者表示自己获得读写权限

                    // bufferSize 1024 * 64：写缓存填满，或者 fs.Flush、fs.Close 时调用 IO 写入；
                    // 但是 IO 也会缓存后再真正执行写盘操作。如果一次写入直接超过缓存，通常会绕过该缓存直接写入。

                    long authenticatedDataOffset = 0;

                    if (options.Verify != null) // 校验数据写在文件最开头
                    {
                        if (options.Verify.TagLength <= 0)
                            throw new InvalidOperationException("校验码长度必须大于0");

                        fs.Position = options.Verify.TagLength; // 先把校验数据位置空出来
                        authenticatedDataOffset = options.Verify.TagLength;
                    }

                    if (aes != null)
                    {
                        if (salt != null)
                            fs.Write(salt, 0, salt.Length);

                        byte[] iv = GenerateIv();
                        fs.Write(iv, 0, iv.Length);

                        using (CryptoStream cryptoStream = new CryptoStream(
                            fs,
                            CreateEncryptor(aes, iv),
                            CryptoStreamMode.Write,
                            true))
                        {
                            // 上层只负责向明文流写数据；离开 using 后完成 AES 最后一块和 Padding。
                            progressReporter?.BeginStage(
                                FileEncryptStage.Encrypting,
                                plaintextLength);
                            WritePlaintext(
                                cryptoStream,
                                writePlaintext,
                                progressReporter);
                        }
                    }
                    else
                    {
                        progressReporter?.BeginStage(
                            FileEncryptStage.Writing,
                            plaintextLength);
                        WritePlaintext(
                            fs,
                            writePlaintext,
                            progressReporter);
                    }

                    progressReporter?.CompleteStage();

                    // 添加校验 Tag
                    if (options.Verify != null)
                    {
                        long verifyLength = fs.Length - authenticatedDataOffset;
                        progressReporter?.BeginStage(
                            FileEncryptStage.GeneratingTag,
                            verifyLength);

                        // 对 salt、IV 和数据整体校验，防止任何影响解密的内容被篡改。
                        fs.Seek(authenticatedDataOffset, SeekOrigin.Begin);
                        byte[] tag;

                        if (progressReporter == null)
                        {
                            tag = options.Verify.ComputeTag(fs, verifyKey);
                        }
                        else
                        {
                            using var progressStream = new ProgressStream(
                                fs,
                                progressReporter.AddBytes,
                                leaveOpen: true);
                            tag = options.Verify.ComputeTag(progressStream, verifyKey);
                        }

                        progressReporter?.CompleteStage();

                        if (tag == null || tag.Length != options.Verify.TagLength)
                            throw new InvalidDataException("校验算法返回的Tag长度与TagLength不一致");

                        fs.Position = 0;
                        fs.Write(tag, 0, tag.Length);
                    }

                    progressReporter?.Complete();
                }
            }
            finally
            {
                if (disposeAes)
                    aes.Dispose();

                ClearKey(verifyKey);
            }
        }

        /// <summary>
        /// 校验并解密文件，然后把明文字节流交给上层读取。
        /// </summary>
        public static TResult Read<TResult>(
            string path,
            Func<Stream, TResult> readPlaintext,
            ProtectedFileOptions options = null,
            IProgress<FileEncryptProgress> progress = null)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("文件路径不能为空", nameof(path));

            if (readPlaintext == null)
                throw new ArgumentNullException(nameof(readPlaintext));

            options ??= new ProtectedFileOptions();
            ValidateOptions(options);

            FileEncryptProgressReporter progressReporter = progress == null
                ? null
                : new FileEncryptProgressReporter(
                    progress,
                    options.Verify == null ? 1 : 2);

            using (FileStream fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            {
                long authenticatedDataOffset = 0;
                byte[] tag = null;

                if (options.Verify != null)
                {
                    if (options.Verify.TagLength <= 0)
                        throw new InvalidOperationException("校验码长度必须大于0");

                    tag = new byte[options.Verify.TagLength];
                    ReadExactly(fs, tag, 0, tag.Length);
                    authenticatedDataOffset = options.Verify.TagLength;
                }

                Aes aes = options.EncryptionAes;
                bool disposeAes = false;
                byte[] iv = null;
                byte[] encryptionKey = null;
                byte[] verifyKey = CloneKey(options.VerifyKey);

                try
                {
                    if (options.EncryptionPassword != null)
                    {
                        byte[] salt = new byte[SaltLength];
                        ReadExactly(fs, salt, 0, salt.Length);

                        DeriveKeys(
                            options.EncryptionPassword,
                            salt,
                            out encryptionKey,
                            out verifyKey);

                        aes = Aes.Create();
                        aes.Key = encryptionKey;
                        disposeAes = true;
                    }

                    if (aes != null)
                    {
                        iv = new byte[IvLength];
                        ReadExactly(fs, iv, 0, iv.Length);
                    }

                    long plaintextDataOffset = fs.Position;

                    // 校验 Tag
                    if (options.Verify != null)
                    {
                        fs.Seek(authenticatedDataOffset, SeekOrigin.Begin);

                        progressReporter?.BeginStage(
                            FileEncryptStage.VerifyingTag,
                            fs.Length - authenticatedDataOffset);

                        bool verifySucceeded;

                        if (progressReporter == null)
                        {
                            verifySucceeded = options.Verify.VerifyTag(fs, tag, verifyKey);
                        }
                        else
                        {
                            using var progressStream = new ProgressStream(
                                fs,
                                progressReporter.AddBytes,
                                leaveOpen: true);
                            verifySucceeded = options.Verify.VerifyTag(
                                progressStream,
                                tag,
                                verifyKey);
                        }

                        progressReporter?.CompleteStage();

                        if (!verifySucceeded)
                        {
                            throw new InvalidDataException("文件完整及清白校验不通过");
                        }

                        fs.Seek(plaintextDataOffset, SeekOrigin.Begin);
                    }

                    if (aes != null)
                    {
                        progressReporter?.BeginStage(
                            FileEncryptStage.Decrypting,
                            fs.Length - fs.Position);

                        TResult result;

                        ProgressStream progressStream = null;
                        Stream encryptedInput = fs;

                        if (progressReporter != null)
                        {
                            progressStream = new ProgressStream(
                                fs,
                                progressReporter.AddBytes,
                                leaveOpen: true);
                            encryptedInput = progressStream;
                        }

                        using (progressStream)
                        using (CryptoStream cryptoStream = new CryptoStream(
                            encryptedInput,
                            CreateDecryptor(aes, iv),
                            CryptoStreamMode.Read))
                        {
                            result = readPlaintext(cryptoStream);
                        }

                        progressReporter?.CompleteStage();
                        progressReporter?.Complete();
                        return result;
                    }

                    progressReporter?.BeginStage(
                        FileEncryptStage.Reading,
                        fs.Length - fs.Position);

                    TResult plaintextResult;

                    if (progressReporter == null)
                    {
                        plaintextResult = readPlaintext(fs);
                    }
                    else
                    {
                        using var progressStream = new ProgressStream(
                            fs,
                            progressReporter.AddBytes,
                            leaveOpen: true);
                        plaintextResult = readPlaintext(progressStream);
                    }

                    progressReporter?.CompleteStage();
                    progressReporter?.Complete();
                    return plaintextResult;
                }
                finally
                {
                    if (disposeAes)
                        aes.Dispose();

                    ClearKey(encryptionKey);
                    ClearKey(verifyKey);
                }
            }
        }

        private static void WritePlaintext(
            Stream outputStream,
            Action<Stream> writePlaintext,
            FileEncryptProgressReporter progressReporter)
        {
            if (progressReporter == null)
            {
                writePlaintext(outputStream);
                return;
            }

            using var progressStream = new ProgressStream(
                outputStream,
                onWrite: progressReporter.AddBytes,
                leaveOpen: true);
            writePlaintext(progressStream);
        }

        private static Aes CreatePasswordAes(
            string passward,
            out byte[] salt,
            out byte[] verifyKey)
        {
            Aes aes = Aes.Create();

            salt = new byte[SaltLength]; // 一般是16或者32，salt的目的是：1、随机使每个文件真正的密码不同；2、通过 salt + passward 计算真正的 key，增大计算量
                                           // ArrayPool<byte>.Shared.Rent() 需要池化条件：1、64KB以上（85KB是托管判断大文件阈值）；2、高频创建
                                           // 所以这个 salt 直接创建即可，反正也好回收
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            DeriveKeys(passward, salt, out byte[] encryptionKey, out verifyKey);
            aes.Key = encryptionKey;
            ClearKey(encryptionKey);
            return aes;
        }

        private static byte[] GenerateIv()
        {
            byte[] iv = new byte[IvLength];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(iv);
            }

            return iv;
        }

        private static ICryptoTransform CreateEncryptor(Aes aes, byte[] iv)
        {
            byte[] key = aes.Key;
            try
            {
                return aes.CreateEncryptor(key, iv);
            }
            finally
            {
                ClearKey(key);
            }
        }

        private static ICryptoTransform CreateDecryptor(Aes aes, byte[] iv)
        {
            byte[] key = aes.Key;
            try
            {
                return aes.CreateDecryptor(key, iv);
            }
            finally
            {
                ClearKey(key);
            }
        }

        private static void DeriveKeys(
            string password,
            byte[] salt,
            out byte[] encryptionKey,
            out byte[] verifyKey)
        {
            using var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256);

            byte[] keyMaterial = derive.GetBytes(64);
            encryptionKey = new byte[32];
            verifyKey = new byte[32];

            Buffer.BlockCopy(keyMaterial, 0, encryptionKey, 0, 32);
            Buffer.BlockCopy(keyMaterial, 32, verifyKey, 0, 32);
            ClearKey(keyMaterial);
        }

        private static void ReadExactly(
            Stream stream,
            byte[] buffer,
            int offset,
            int count)
        {
            while (count > 0)
            {
                int readCount = stream.Read(buffer, offset, count);

                if (readCount == 0)
                    throw new EndOfStreamException("文件提前结束");

                offset += readCount;
                count -= readCount;
            }
        }

        private static void ValidateOptions(ProtectedFileOptions options)
        {
            if (options.EncryptionPassword != null && options.EncryptionAes != null)
            {
                throw new InvalidOperationException(
                    "EncryptionPassword 与 EncryptionAes 不能同时设置");
            }

            if (options.EncryptionPassword != null && options.VerifyKey != null)
            {
                throw new InvalidOperationException(
                    "密码模式会自动派生校验 Key，不能同时设置 VerifyKey");
            }

            if (options.Verify is HMACVerify &&
                options.EncryptionPassword == null &&
                options.EncryptionAes == null)
            {
                throw new InvalidOperationException(
                    "HMACVerify 必须与 EncryptionPassword 或 EncryptionAes 一起使用");
            }

            if (options.Verify is HMACVerify &&
                options.EncryptionPassword == null &&
                (options.VerifyKey == null || options.VerifyKey.Length == 0))
            {
                throw new InvalidOperationException(
                    "直接 AES 模式使用 HMACVerify 时必须提供独立的 VerifyKey");
            }
        }

        private static byte[] CloneKey(byte[] key)
        {
            return key == null ? null : (byte[])key.Clone();
        }

        private static void ClearKey(byte[] key)
        {
            if (key != null)
                Array.Clear(key, 0, key.Length);
        }
    }
}
