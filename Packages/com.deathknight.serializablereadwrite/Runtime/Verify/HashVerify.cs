using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace SerializableReadWrite
{
    public class HashVerify : IVerify
    {
        public bool NeedPassword => false;
        public int TagLength => 32;

        public byte[] ComputeTag(byte[] data, byte[] passward = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        public byte[] ComputeTag(Stream data, byte[] passward = null)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using (SHA256 sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        public bool VerifyTag(
            byte[] data,
            byte[] tag,
            byte[] passward = null)
        {
            if (tag == null || tag.Length != TagLength)
                return false;

            byte[] calculatedTag = ComputeTag(data);
            return CryptographicOperations.FixedTimeEquals(calculatedTag, tag);
        }

        public bool VerifyTag(
            Stream data,
            byte[] tag,
            byte[] passward = null)
        {
            if (tag == null || tag.Length != TagLength)
                return false;

            byte[] calculatedTag = ComputeTag(data);
            return CryptographicOperations.FixedTimeEquals(calculatedTag, tag);
        }

        public Task<byte[]> ComputeTagAsync(
            Stream data,
            byte[] passward = null,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return ComputeTagAsyncCore(
                data,
                ReadWriteStage.GeneratingTag,
                progress,
                cancellationToken);
        }

        public async Task<bool> VerifyTagAsync(
            Stream data,
            byte[] tag,
            byte[] passward = null,
            IProgress<ReadWriteProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (tag == null || tag.Length != TagLength)
                return false;

            byte[] calculatedTag = await ComputeTagAsyncCore(
                data,
                ReadWriteStage.VerifyingTag,
                progress,
                cancellationToken).ConfigureAwait(false);

            return CryptographicOperations.FixedTimeEquals(calculatedTag, tag);
        }

        private static async Task<byte[]> ComputeTagAsyncCore(
            Stream data,
            ReadWriteStage stage,
            IProgress<ReadWriteProgress> progress,
            CancellationToken cancellationToken)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (!data.CanRead)
                throw new ArgumentException("校验数据流必须可读", nameof(data));

            using (SHA256 sha256 = SHA256.Create())
            {
                long totalBytes = AsyncStreamProgress.GetRemainingLength(data);
                long processedBytes = 0;
                byte[] buffer = new byte[AsyncStreamProgress.BufferSize];
                AsyncStreamProgress.Report(progress, stage, 0, totalBytes);

                while (true)
                {
                    int readCount = await data.ReadAsync(
                        buffer,
                        0,
                        buffer.Length,
                        cancellationToken).ConfigureAwait(false);

                    if (readCount == 0)
                        break;

                    sha256.TransformBlock(
                        buffer,
                        0,
                        readCount,
                        buffer,
                        0);
                    processedBytes += readCount;
                    AsyncStreamProgress.Report(
                        progress,
                        stage,
                        processedBytes,
                        totalBytes);
                }

                sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                AsyncStreamProgress.Report(
                    progress,
                    stage,
                    totalBytes >= 0 ? totalBytes : processedBytes,
                    totalBytes);
                return sha256.Hash;
            }
        }
    }
}
