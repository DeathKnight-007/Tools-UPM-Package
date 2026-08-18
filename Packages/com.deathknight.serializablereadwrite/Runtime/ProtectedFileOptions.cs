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
        /// null 代表不使用校验，非空则使用指定的校验算法。
        /// </summary>
        public IVerify Verify { get; set; }

    }
}
