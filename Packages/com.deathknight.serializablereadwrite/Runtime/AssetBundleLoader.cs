using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace SerializableReadWrite
{
    /// <summary>
    /// 小型受保护 AssetBundle 的读取适配器。
    /// </summary>
    public static class AssetBundleLoader
    {
        private static AssetBundleLoaderRunner runner;

        /// <summary>
        /// 同步校验、解密并加载 AssetBundle。
        /// </summary>
        public static AssetBundle Load(
            string protectedPath,
            string passward = null,
            IVerify verify = null,
            uint crc = 0)
        {
            byte[] bundleBytes = FileEncrypt.DecryptToBytes(
                protectedPath,
                passward,
                verify);

            AssetBundle assetBundle = AssetBundle.LoadFromMemory(bundleBytes, crc);

            if (assetBundle == null)
                throw new InvalidDataException("解密后的数据不是有效的AssetBundle");

            return assetBundle;
        }

        /// <summary>
        /// 在工作线程中校验和解密，然后回到 Unity 主线程异步加载 AssetBundle。
        /// 此方法必须从 Unity 主线程调用。
        /// </summary>
        public static Task<AssetBundle> LoadAsync(
            string protectedPath,
            string passward = null,
            IVerify verify = null,
            uint crc = 0)
        {
            return EnsureRunner().LoadAsync(
                protectedPath,
                passward,
                verify,
                crc);
        }

        /// <summary>
        /// 使用调用方提供的 AES 同步校验、解密并加载 AssetBundle。
        /// </summary>
        public static AssetBundle LoadWithAes(
            string protectedPath,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null,
            uint crc = 0)
        {
            byte[] bundleBytes = FileEncrypt.DecryptToBytesWithAes(
                protectedPath,
                aes,
                verify,
                verifyKey);

            AssetBundle assetBundle = AssetBundle.LoadFromMemory(bundleBytes, crc);

            if (assetBundle == null)
                throw new InvalidDataException("解密后的数据不是有效的AssetBundle");

            return assetBundle;
        }

        /// <summary>
        /// 使用调用方提供的 AES 在工作线程中校验和解密，然后回到 Unity 主线程加载 AssetBundle。
        /// 此方法必须从 Unity 主线程调用，AES 在任务结束前不能被释放或并发使用。
        /// </summary>
        public static Task<AssetBundle> LoadAsyncWithAes(
            string protectedPath,
            Aes aes,
            IVerify verify = null,
            byte[] verifyKey = null,
            uint crc = 0)
        {
            return EnsureRunner().LoadAsyncWithAes(
                protectedPath,
                aes,
                verify,
                verifyKey,
                crc);
        }

        private static AssetBundleLoaderRunner EnsureRunner()
        {
            if (runner != null)
                return runner;

            var gameObject = new GameObject("[AssetBundleLoader]");
            gameObject.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);

            runner = gameObject.AddComponent<AssetBundleLoaderRunner>();
            return runner;
        }

        internal static void NotifyRunnerDestroyed(AssetBundleLoaderRunner destroyedRunner)
        {
            if (runner == destroyedRunner)
                runner = null;
        }

        // 兼容关闭 Domain Reload 的进入播放模式设置，避免静态字段保留上一次运行的对象引用。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            runner = null;
        }
    }
}
