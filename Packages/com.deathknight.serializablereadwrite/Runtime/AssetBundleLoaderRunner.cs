using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace SerializableReadWrite
{
    /// <summary>
    /// AssetBundleLoader 内部使用的主线程协程执行器。
    /// </summary>
    internal sealed class AssetBundleLoaderRunner : MonoBehaviour
    {
        private readonly HashSet<TaskCompletionSource<AssetBundle>> pendingRequests =
            new HashSet<TaskCompletionSource<AssetBundle>>();

        internal Task<AssetBundle> LoadAsync(
            string protectedPath,
            string passward,
            IVerify verify,
            uint crc)
        {
            var completionSource = new TaskCompletionSource<AssetBundle>();
            pendingRequests.Add(completionSource);

            StartCoroutine(LoadCoroutine(
                completionSource,
                () => FileEncrypt.DecryptToBytes(
                    protectedPath,
                    passward,
                    verify),
                crc));

            return completionSource.Task;
        }

        internal Task<AssetBundle> LoadAsyncWithAes(
            string protectedPath,
            Aes aes,
            IVerify verify,
            byte[] verifyKey,
            uint crc)
        {
            var completionSource = new TaskCompletionSource<AssetBundle>();
            pendingRequests.Add(completionSource);

            StartCoroutine(LoadCoroutine(
                completionSource,
                () => FileEncrypt.DecryptToBytesWithAes(
                    protectedPath,
                    aes,
                    verify,
                    verifyKey),
                crc));

            return completionSource.Task;
        }

        private IEnumerator LoadCoroutine(
            TaskCompletionSource<AssetBundle> completionSource,
            Func<byte[]> decrypt,
            uint crc)
        {
            // 工作线程中只执行纯 C# 的文件读取、校验和解密，不调用 Unity API。
            Task<byte[]> decryptTask = Task.Run(decrypt);

            while (!decryptTask.IsCompleted)
                yield return null;

            byte[] bundleBytes;

            try
            {
                // GetAwaiter().GetResult() 保留原始异常，不额外包装成 AggregateException。
                bundleBytes = decryptTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                CompleteWithException(completionSource, exception);
                yield break;
            }

            AssetBundleCreateRequest createRequest;

            try
            {
                // Coroutine 在主线程执行，因此 Unity API 只会在主线程调用。
                createRequest = AssetBundle.LoadFromMemoryAsync(bundleBytes, crc);
            }
            catch (Exception exception)
            {
                bundleBytes = null;
                CompleteWithException(completionSource, exception);
                yield break;
            }

            yield return createRequest;

            AssetBundle assetBundle = createRequest.assetBundle;
            bundleBytes = null;

            if (assetBundle == null)
            {
                CompleteWithException(
                    completionSource,
                    new InvalidDataException("解密后的数据不是有效的AssetBundle"));
                yield break;
            }

            pendingRequests.Remove(completionSource);
            completionSource.TrySetResult(assetBundle);
        }

        private void CompleteWithException(
            TaskCompletionSource<AssetBundle> completionSource,
            Exception exception)
        {
            pendingRequests.Remove(completionSource);
            completionSource.TrySetException(exception);
        }

        private void OnDestroy()
        {
            var exception = new ObjectDisposedException(
                nameof(AssetBundleLoaderRunner),
                "AssetBundle加载器已被销毁");

            foreach (TaskCompletionSource<AssetBundle> request in pendingRequests)
            {
                request.TrySetException(exception);
            }

            pendingRequests.Clear();
            AssetBundleLoader.NotifyRunnerDestroyed(this);
        }
    }
}
