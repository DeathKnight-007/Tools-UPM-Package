using System.Security.Cryptography;

namespace SerializableReadWrite
{
    /// <summary>
    /// 受保护文件的加密和校验配置。
    /// </summary>
    public sealed class ProtectedFileOptions
    {
        /// <summary>
        /// null 代表不使用加密，非空则使用 AES 加密。
        /// </summary>
        public string EncryptionPassword { get; set; }

        /// <summary>
        /// 直接用于加解密的 AES 实例。设置后不会执行 PBKDF2。
        /// EncryptionAes 与 EncryptionPassword 不能同时设置，实例的释放由调用方负责。
        /// 底层只使用它的 Key、Mode 和 Padding；IV 会在写入时逐次随机生成并存入文件。
        /// </summary>
        public Aes EncryptionAes { get; set; }

        /// <summary>
        /// 直接 AES 模式下传给校验器的独立 Key。
        /// 使用 HMACVerify 时必须提供；底层不会修改或清除此数组。
        /// </summary>
        public byte[] VerifyKey { get; set; }

        /// <summary>
        /// null 代表不使用校验，非空则使用指定的校验算法。
        /// </summary>
        public IVerify Verify { get; set; }

    }
}
