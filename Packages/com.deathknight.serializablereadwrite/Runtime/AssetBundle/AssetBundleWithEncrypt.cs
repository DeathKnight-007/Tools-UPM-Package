using System;
using System.IO;
using UnityEngine;

namespace SerializableReadWrite
{
    /// <summary>
    /// 使用指定的加密器读取并加载加密的 AssetBundle。
    /// </summary>
    public class AssetBundleWithEncrypt
    {
        private const int FileBufferSize = 1024 * 64;

        private IEncrypt Encrypt { get; }

        public AssetBundleWithEncrypt(IEncrypt encrypt)
        {
            Encrypt = encrypt ?? throw new ArgumentNullException(nameof(encrypt));
        }

        /// <summary>
        /// 解密文件并同步加载 AssetBundle。
        /// 必须在允许调用 Unity API 的线程中执行。
        /// </summary>
        public AssetBundle Load(string encryptBundlePath)
        {
            if (string.IsNullOrEmpty(encryptBundlePath))
            {
                throw new ArgumentException(
                    "加密 AssetBundle 路径不能为空",
                    nameof(encryptBundlePath));
            }

            byte[] bundleBytes;
            using (var encryptStream = new FileStream(
                encryptBundlePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize))
            {
                bundleBytes = Encrypt.Decrypt(encryptStream);
            }

            AssetBundle assetBundle = AssetBundle.LoadFromMemory(bundleBytes);
            if (assetBundle == null)
            {
                throw new InvalidDataException(
                    "解密后的数据不是有效的 AssetBundle");
            }

            return assetBundle;
        }
    }
}
