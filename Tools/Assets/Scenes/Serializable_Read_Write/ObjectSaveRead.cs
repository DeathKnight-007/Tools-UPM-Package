using System.IO;
using System.Text;
using Unity.Plastic.Newtonsoft.Json;
using JsonSerializer = Unity.Plastic.Newtonsoft.Json.JsonSerializer;

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
        /// <param name="verifyPassward">null代表不使用校验密码，非空则使用校验密码，防止有人修改校验码</param>
        public static void Save<T>(
            string path,
            T data,
            string passward = null,
            IVerify verify = null,
            string verifyPassward = null)
        {
            // 1、写入内存占用以及效率
            // 2、异步写入
            // 3、流式加密
            ProtectedFile.Write(
                path,
                plaintextStream =>
                {
                    using (StreamWriter sw = new StreamWriter(
                        plaintextStream,
                        new UTF8Encoding(false),
                        1024 * 16,
                        true))
                    using (JsonTextWriter jw = new JsonTextWriter(sw))
                    {
                        // 底层明文流属于 ProtectedFile，JSON 层只能刷新，不能关闭它。
                        jw.CloseOutput = false;

                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(jw, data);

                        jw.Flush();
                        sw.Flush();
                    }
                },
                CreateOptions(passward, verify, verifyPassward));
        }

        public static T Read<T>(
            string path,
            string passward = null,
            IVerify verify = null,
            string verifyPassward = null)
        {
            return ProtectedFile.Read(
                path,
                plaintextStream =>
                {
                    using (StreamReader sr = new StreamReader(
                        plaintextStream,
                        Encoding.UTF8,
                        false,
                        1024 * 16,
                        true))
                    using (JsonTextReader jr = new JsonTextReader(sr))
                    {
                        // 底层明文流属于 ProtectedFile，JSON 层不能关闭它。
                        jr.CloseInput = false;

                        JsonSerializer serializer = new JsonSerializer();
                        return serializer.Deserialize<T>(jr);
                    }
                },
                CreateOptions(passward, verify, verifyPassward));
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
    }
}
