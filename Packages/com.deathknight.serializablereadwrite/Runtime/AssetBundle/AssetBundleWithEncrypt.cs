using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SerializableReadWrite
{
    /// <summary>
    /// 使用指定的加密器读取并加载加密的 AssetBundle。
    /// </summary>
    public class AssetBundleWithEncrypt
    {
        private const int FileBufferSize = 1024 * 64;
        private const long AssetBundleProgressScale = 1000;

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
            ValidatePath(encryptBundlePath);

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

        /// <summary>
        /// 异步解密文件并加载 AssetBundle。
        /// 必须从 Unity 主线程调用。
        /// </summary>
        public async Task<AssetBundle> LoadAsync(
            string encryptBundlePath,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePath(encryptBundlePath);

            byte[] bundleBytes;
            using (var encryptStream = new FileStream(
                encryptBundlePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize,
                true))
            {
                bundleBytes = await Encrypt.DecryptAsync(
                    encryptStream,
                    progress,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.LoadingAssetBundle,
                0,
                AssetBundleProgressScale);

            AssetBundleCreateRequest request =
                AssetBundle.LoadFromMemoryAsync(bundleBytes);
            while (!request.isDone)
            {
                AsyncStreamProgress.Report(
                    progress,
                    ReadWriteStage.LoadingAssetBundle,
                    (long)(request.progress * AssetBundleProgressScale),
                    AssetBundleProgressScale);
                await Task.Yield();
            }

            AsyncStreamProgress.Report(
                progress,
                ReadWriteStage.LoadingAssetBundle,
                AssetBundleProgressScale,
                AssetBundleProgressScale);

            AssetBundle assetBundle = request.assetBundle;
            if (assetBundle == null)
            {
                throw new InvalidDataException(
                    "解密后的数据不是有效的 AssetBundle");
            }

            return assetBundle;
        }

        private static void ValidatePath(string encryptBundlePath)
        {
            if (string.IsNullOrEmpty(encryptBundlePath))
            {
                throw new ArgumentException(
                    "加密 AssetBundle 路径不能为空",
                    nameof(encryptBundlePath));
            }
        }
    }
}
