using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SerializableReadWrite
{
    /// <summary>
    /// 负责明文字节流与受保护文件之间的转换。
    /// 不关心上层写入的是 JSON、图片还是其他二进制数据。
    /// 文件布局为：[Tag][salt][IV][data]，不存在的可选部分不写入。
    /// 当前校验范围只包含 data；启用 AES 时，data 是密文。
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

            Aes aes = null;
            byte[] salt = null;

            // 加密
            if (options.EncryptionPassword != null)
            {
                aes = GetAes(options.EncryptionPassword, out salt);
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

                    long dataOffset = 0; // 数据开始的位置；启用 AES 时，这里也是密文开始的位置

                    if (options.Verify != null) // 校验数据写在文件最开头
                    {
                        if (options.Verify.TagLength <= 0)
                            throw new InvalidOperationException("校验码长度必须大于0");

                        fs.Position = options.Verify.TagLength; // 先把校验数据位置空出来
                        dataOffset += options.Verify.TagLength;
                    }

                    if (aes != null)
                    {
                        fs.Write(salt, 0, salt.Length);

                        byte[] iv = aes.IV;
                        fs.Write(iv, 0, iv.Length);

                        dataOffset += salt.Length;
                        dataOffset += iv.Length;

                        using (CryptoStream cryptoStream = new CryptoStream(
                            fs,
                            aes.CreateEncryptor(),
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
                        long verifyLength = fs.Length - dataOffset;
                        progressReporter?.BeginStage(
                            FileEncryptStage.GeneratingTag,
                            verifyLength);

                        fs.Seek(dataOffset, SeekOrigin.Begin); // 只对数据校验；启用 AES 时只对密文校验
                        byte[] verifyKey = GetVerifyKey(options.VerifyKey);
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
                aes?.Dispose();
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
                long headerOffset = 0;

                // 校验 Tag
                if (options.Verify != null)
                {
                    if (options.Verify.TagLength <= 0)
                        throw new InvalidOperationException("校验码长度必须大于0");

                    headerOffset += options.Verify.TagLength;

                    if (options.EncryptionPassword != null)
                    {
                        headerOffset += SaltLength + IvLength; // AES 加密的 salt + IV
                    }

                    byte[] tag = new byte[options.Verify.TagLength];
                    ReadExactly(fs, tag, 0, tag.Length); // 读取出校验码

                    fs.Seek(headerOffset, SeekOrigin.Begin);

                    progressReporter?.BeginStage(
                        FileEncryptStage.VerifyingTag,
                        fs.Length - headerOffset);

                    byte[] verifyKey = GetVerifyKey(options.VerifyKey);
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

                    fs.Seek(options.Verify.TagLength, SeekOrigin.Begin); // 指针移动到校验码末尾
                }

                if (options.EncryptionPassword != null)
                {
                    using Aes aes = Aes.Create();

                    byte[] salt = new byte[SaltLength];
                    ReadExactly(fs, salt, 0, salt.Length);

                    byte[] iv = new byte[IvLength];
                    ReadExactly(fs, iv, 0, iv.Length);

                    aes.IV = iv;
                    aes.Key = DeriveKey(options.EncryptionPassword, salt);

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
                        aes.CreateDecryptor(),
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

        private static Aes GetAes(string passward, out byte[] salt)
        {
            // 使用 AES 加密
            Aes aes = Aes.Create();

            aes.GenerateIV(); // aes.IV 可以自动生成，也可以自己生成，但是没必要自己生成
                              // 密文初始化向量保存在加密后的文件开头，保证数据相同、密码相同，但是加密结果不同
            byte[] iv = aes.IV; // 长度是16

            salt = new byte[SaltLength]; // 一般是16或者32，salt的目的是：1、随机使每个文件真正的密码不同；2、通过 salt + passward 计算真正的 key，增大计算量
                                           // ArrayPool<byte>.Shared.Rent() 需要池化条件：1、64KB以上（85KB是托管判断大文件阈值）；2、高频创建
                                           // 所以这个 salt 直接创建即可，反正也好回收
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            aes.Key = DeriveKey(passward, salt); // 密码长度固定派生为32字节，让用户短密码也能生成32字节密码
                                                  // aes.GenerateKey(); 密码也可以自动生成
            return aes;
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using var derive = new Rfc2898DeriveBytes(
                password,
                salt,
                100000);

            return derive.GetBytes(32); // 32字节，AES-256
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

        private static byte[] GetVerifyKey(string verifyKey)
        {
            return verifyKey == null ? null : Encoding.UTF8.GetBytes(verifyKey);
        }
    }
}
