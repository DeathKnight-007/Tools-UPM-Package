using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SerializableReadWrite
{
    /// <summary>
    /// 对象与 JSON 明文字节流之间的读写适配器。
    /// 文件、AES、salt、IV 和 Tag 由 ProtectedFile 负责。
    /// </summary>
    public static class ObjectSaveRead
    {
        /// <summary>
        /// 保存数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="data"></param>
        /// <param name="passward">null代表不使用加密，有密码则使用AES加密</param>
        /// <param name="verify">null代表不使用校验，非空则使用校验码，防止文件缺失或者被人修改</param>
        public static void Save<T>(
            string path,
            T data,
            string passward = null,
            IVerify verify = null)
        {
            // 1、写入内存占用以及效率
            // 2、异步写入
            // 3、流式加密
            ProtectedFile.Write(
                path,
                plaintextStream => SerializeJson(plaintextStream, data),
                CreateOptions(passward, verify));
        }

        public static T Read<T>(
            string path,
            string passward = null,
            IVerify verify = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream => DeserializeJson<T>(plaintextStream),
                CreateOptions(passward, verify));
        }

        /// <summary>
        /// 使用调用方提供的 AES 保存对象，不执行密码密钥派生。
        /// AES 的释放由调用方负责；使用 HMACVerify 时必须提供独立的 verifyKey。
        /// </summary>
        public static void SaveWithAes<T>(
            string path,
            T data,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            ProtectedFile.Write(
                path,
                plaintextStream => SerializeJson(plaintextStream, data),
                CreateOptions(aes, verify, verifyKey));
        }

        /// <summary>
        /// 使用调用方提供的 AES 读取对象，不执行密码密钥派生。
        /// </summary>
        public static T ReadWithAes<T>(
            string path,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream => DeserializeJson<T>(plaintextStream),
                CreateOptions(aes, verify, verifyKey));
        }

        private static void SerializeJson<T>(Stream plaintextStream, T data)
        {
            using (StreamWriter sw = new StreamWriter(
                plaintextStream,
                new UTF8Encoding(false),
                1024 * 16,
                true))
            using (JsonTextWriter jw = new JsonTextWriter(sw))
            {
                jw.CloseOutput = false;

                JsonSerializer serializer = new JsonSerializer();
                serializer.Serialize(jw, data);

                jw.Flush();
                sw.Flush();
            }
        }

        private static T DeserializeJson<T>(Stream plaintextStream)
        {
            using (StreamReader sr = new StreamReader(
                plaintextStream,
                Encoding.UTF8,
                false,
                1024 * 16,
                true))
            using (JsonTextReader jr = new JsonTextReader(sr))
            {
                jr.CloseInput = false;

                JsonSerializer serializer = new JsonSerializer();
                return serializer.Deserialize<T>(jr);
            }
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
