using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SerializableReadWrite
{
    /// <summary>
    /// 对象与 byte[] 之间的序列化适配器。
    ///
    /// 数据格式与 ObjectSaveRead/ProtectedFile 保持一致：
    /// 密码模式为 [Tag][salt][IV][data]，直接 AES 模式为 [Tag][IV][data]。
    /// 未启用的可选部分不会写入；data 是 JSON 明文或 AES 密文。
    /// </summary>
    public static class ObjectSerilizeToByte
    {
        private const int SaltLength = 16;
        private const int IvLength = 16;
        private const int JsonBufferSize = 1024 * 16;

        /// <summary>
        /// 把对象序列化到 buffer，从 offset 开始写入，并返回实际写入的字节数。
        /// passward 为 null 时不加密；verify 为 null 时不生成校验 Tag。
        /// buffer 剩余空间不足时抛出 ArgumentException。
        /// </summary>
        public static int Serialize<T>(
            T data,
            byte[] buffer,
            int offset,
            string passward = null,
            IVerify verify = null)
        {
            return SerializeCore(
                data,
                buffer,
                offset,
                passward,
                null,
                verify,
                null);
        }

        /// <summary>
        /// 使用调用方提供的 AES 把对象序列化到 buffer，不执行密码密钥派生。
        /// AES 的释放由调用方负责；使用 HMACVerify 时必须提供独立的 verifyKey。
        /// </summary>
        public static int SerializeWithAes<T>(
            T data,
            byte[] buffer,
            int offset,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            if (aes == null)
                throw new ArgumentNullException(nameof(aes));

            return SerializeCore(
                data,
                buffer,
                offset,
                null,
                aes,
                verify,
                verifyKey);
        }

        private static int SerializeCore<T>(
            T data,
            byte[] buffer,
            int offset,
            string passward,
            Aes encryptionAes,
            IVerify verify,
            byte[] directVerifyKey)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            ValidateBufferRange(buffer, offset, buffer.Length - offset);
            ValidateOptions(passward, encryptionAes, verify, directVerifyKey);

            Aes aes = encryptionAes;
            bool disposeAes = false;
            byte[] salt = null;
            byte[] verifyKey = CloneKey(directVerifyKey);

            if (passward != null)
            {
                aes = CreateAes(passward, out salt, out verifyKey);
                disposeAes = true;
            }

            try
            {
                using var outputStream = new MemoryStream(
                    buffer,
                    offset,
                    buffer.Length - offset,
                    true,
                    true);

                // 基于数组片段创建的 MemoryStream 初始 Length 等于片段容量。
                // 清零逻辑长度后，Length 才能表示本次实际写入量。
                outputStream.SetLength(0);
                long authenticatedDataOffset = 0;

                if (verify != null)
                {
                    ValidateVerify(verify);
                    outputStream.Position = verify.TagLength;
                    authenticatedDataOffset = verify.TagLength;
                }

                if (aes != null)
                {
                    if (salt != null)
                        outputStream.Write(salt, 0, salt.Length);

                    byte[] iv = GenerateIv();
                    outputStream.Write(iv, 0, iv.Length);

                    using (var cryptoStream = new CryptoStream(
                        outputStream,
                        CreateEncryptor(aes, iv),
                        CryptoStreamMode.Write,
                        true))
                    {
                        SerializeJson(cryptoStream, data);
                    }
                }
                else
                {
                    SerializeJson(outputStream, data);
                }

                if (verify != null)
                {
                    outputStream.Position = authenticatedDataOffset;
                    byte[] tag = verify.ComputeTag(outputStream, verifyKey);

                    if (tag == null || tag.Length != verify.TagLength)
                    {
                        throw new InvalidDataException(
                            "校验算法返回的Tag长度与TagLength不一致");
                    }

                    outputStream.Position = 0;
                    outputStream.Write(tag, 0, tag.Length);
                }

                return checked((int)outputStream.Length);
            }
            catch (NotSupportedException exception)
            {
                throw new ArgumentException(
                    "buffer 从 offset 开始的剩余空间不足，无法容纳序列化后的完整数据。",
                    nameof(buffer),
                    exception);
            }
            finally
            {
                if (disposeAes)
                    aes.Dispose();

                ClearKey(verifyKey);
            }
        }

        /// <summary>
        /// 从 data 的指定片段中校验、解密并反序列化对象。
        /// </summary>
        public static T Deserialize<T>(
            byte[] data,
            int offset,
            int count,
            string passward = null,
            IVerify verify = null)
        {
            return DeserializeCore<T>(
                data,
                offset,
                count,
                passward,
                null,
                verify,
                null);
        }

        /// <summary>
        /// 使用调用方提供的 AES 从 byte[] 片段中校验、解密并反序列化对象。
        /// </summary>
        public static T DeserializeWithAes<T>(
            byte[] data,
            int offset,
            int count,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            if (aes == null)
                throw new ArgumentNullException(nameof(aes));

            return DeserializeCore<T>(
                data,
                offset,
                count,
                null,
                aes,
                verify,
                verifyKey);
        }

        private static T DeserializeCore<T>(
            byte[] data,
            int offset,
            int count,
            string passward,
            Aes encryptionAes,
            IVerify verify,
            byte[] directVerifyKey)
        {
            ValidateBufferRange(data, offset, count);
            ValidateOptions(passward, encryptionAes, verify, directVerifyKey);

            using var inputStream = new MemoryStream(
                data,
                offset,
                count,
                false,
                false);

            long authenticatedDataOffset = 0;
            byte[] tag = null;

            if (verify != null)
            {
                ValidateVerify(verify);
                tag = new byte[verify.TagLength];
                ReadExactly(inputStream, tag, 0, tag.Length);
                authenticatedDataOffset = verify.TagLength;
            }

            byte[] iv = null;
            byte[] encryptionKey = null;
            byte[] verifyKey = CloneKey(directVerifyKey);
            Aes aes = encryptionAes;
            bool disposeAes = false;

            try
            {
                if (passward != null)
                {
                    byte[] salt = new byte[SaltLength];
                    ReadExactly(inputStream, salt, 0, salt.Length);

                    DeriveKeys(
                        passward,
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
                    ReadExactly(inputStream, iv, 0, iv.Length);
                }

                long plaintextDataOffset = inputStream.Position;

                if (verify != null)
                {
                    inputStream.Position = authenticatedDataOffset;

                    if (!verify.VerifyTag(inputStream, tag, verifyKey))
                    {
                        throw new InvalidDataException(
                            "数据完整及清白校验不通过");
                    }

                    inputStream.Position = plaintextDataOffset;
                }

                if (aes == null)
                {
                    return DeserializeJson<T>(inputStream);
                }

                using var cryptoStream = new CryptoStream(
                    inputStream,
                    CreateDecryptor(aes, iv),
                    CryptoStreamMode.Read);

                return DeserializeJson<T>(cryptoStream);
            }
            finally
            {
                if (disposeAes)
                    aes.Dispose();

                ClearKey(encryptionKey);
                ClearKey(verifyKey);
            }
        }

        private static void SerializeJson<T>(Stream stream, T data)
        {
            using (var streamWriter = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                JsonBufferSize,
                true))
            using (var jsonWriter = new JsonTextWriter(streamWriter))
            {
                jsonWriter.CloseOutput = false;

                var serializer = new JsonSerializer();
                serializer.Serialize(jsonWriter, data);

                jsonWriter.Flush();
                streamWriter.Flush();
            }
        }

        private static T DeserializeJson<T>(Stream stream)
        {
            using (var streamReader = new StreamReader(
                stream,
                Encoding.UTF8,
                false,
                JsonBufferSize,
                true))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                jsonReader.CloseInput = false;

                var serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jsonReader);
            }
        }

        private static Aes CreateAes(
            string passward,
            out byte[] salt,
            out byte[] verifyKey)
        {
            Aes aes = Aes.Create();

            salt = new byte[SaltLength];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
            }

            DeriveKeys(
                passward,
                salt,
                out byte[] encryptionKey,
                out verifyKey);

            try
            {
                aes.Key = encryptionKey;
            }
            finally
            {
                ClearKey(encryptionKey);
            }

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
                {
                    throw new EndOfStreamException("数据提前结束");
                }

                offset += readCount;
                count -= readCount;
            }
        }

        private static void ValidateBufferRange(
            byte[] buffer,
            int offset,
            int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0 || count > buffer.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private static void ValidateOptions(
            string passward,
            Aes aes,
            IVerify verify,
            byte[] verifyKey)
        {
            if (passward != null && aes != null)
            {
                throw new InvalidOperationException(
                    "passward 与 aes 不能同时设置");
            }

            if (passward != null && verifyKey != null)
            {
                throw new InvalidOperationException(
                    "密码模式会自动派生校验 Key，不能同时设置 verifyKey");
            }

            if (verify is HMACVerify &&
                passward == null &&
                aes == null)
            {
                throw new InvalidOperationException(
                    "HMACVerify 必须与 passward 或 aes 一起使用");
            }

            if (verify is HMACVerify &&
                passward == null &&
                (verifyKey == null || verifyKey.Length == 0))
            {
                throw new InvalidOperationException(
                    "直接 AES 模式使用 HMACVerify 时必须提供独立的 verifyKey");
            }
        }

        private static byte[] CloneKey(byte[] key)
        {
            return key == null ? null : (byte[])key.Clone();
        }

        private static void ValidateVerify(IVerify verify)
        {
            if (verify.TagLength <= 0)
            {
                throw new InvalidOperationException("校验码长度必须大于0");
            }
        }

        private static void ClearKey(byte[] key)
        {
            if (key != null)
            {
                Array.Clear(key, 0, key.Length);
            }
        }
    }
}
