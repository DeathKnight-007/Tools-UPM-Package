using System;

namespace SerializableReadWrite
{
    /// <summary>
    /// 异步读写操作当前所处的阶段。
    /// 每个阶段的进度独立从 0 计算到 1。
    /// </summary>
    public enum ReadWriteStage
    {
        DerivingKey,
        Encrypting,
        GeneratingTag,
        VerifyingTag,
        Decrypting,
        Serializing,
        Deserializing,
        LoadingAssetBundle
    }

    /// <summary>
    /// 异步读写操作的阶段进度。
    /// TotalBytes 为 -1 时表示当前阶段的总长度未知。
    /// </summary>
    public sealed class ReadWriteProgress
    {
        public ReadWriteProgress(
            ReadWriteStage stage,
            long processedBytes,
            long totalBytes)
        {
            if (processedBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(processedBytes));
            if (totalBytes < -1)
                throw new ArgumentOutOfRangeException(nameof(totalBytes));

            Stage = stage;
            ProcessedBytes = processedBytes;
            TotalBytes = totalBytes;
        }

        public ReadWriteStage Stage { get; }
        public long ProcessedBytes { get; }
        public long TotalBytes { get; }
        public bool IsLengthKnown => TotalBytes >= 0;

        /// <summary>
        /// 当前阶段的完成比例。总长度未知时返回 -1。
        /// </summary>
        public float Progress
        {
            get
            {
                if (!IsLengthKnown)
                    return -1f;
                if (TotalBytes == 0)
                    return 1f;

                return (float)Math.Min(ProcessedBytes, TotalBytes) / TotalBytes;
            }
        }
    }

    internal static class AsyncStreamProgress
    {
        internal const int BufferSize = 1024 * 64;

        internal static long GetRemainingLength(System.IO.Stream stream)
        {
            return stream.CanSeek ? stream.Length - stream.Position : -1;
        }

        internal static void Report(
            IProgress<ReadWriteProgress> progress,
            ReadWriteStage stage,
            long processedBytes,
            long totalBytes)
        {
            progress?.Report(new ReadWriteProgress(
                stage,
                processedBytes,
                totalBytes));
        }

        internal static async System.Threading.Tasks.Task<long> CopyToAsync(
            System.IO.Stream source,
            System.IO.Stream destination,
            ReadWriteStage stage,
            IProgress<ReadWriteProgress> progress,
            System.Threading.CancellationToken cancellationToken,
            long totalBytes = -1)
        {
            if (totalBytes < 0)
                totalBytes = GetRemainingLength(source);

            byte[] buffer = new byte[BufferSize];
            long processedBytes = 0;
            Report(progress, stage, 0, totalBytes);

            while (true)
            {
                int readCount = await source.ReadAsync(
                    buffer,
                    0,
                    buffer.Length,
                    cancellationToken).ConfigureAwait(false);

                if (readCount == 0)
                    break;

                await destination.WriteAsync(
                    buffer,
                    0,
                    readCount,
                    cancellationToken).ConfigureAwait(false);

                processedBytes += readCount;
                Report(progress, stage, processedBytes, totalBytes);
            }

            Report(
                progress,
                stage,
                totalBytes >= 0 ? totalBytes : processedBytes,
                totalBytes);
            return processedBytes;
        }

        internal static async System.Threading.Tasks.Task WriteAsync(
            System.IO.Stream destination,
            byte[] data,
            ReadWriteStage stage,
            IProgress<ReadWriteProgress> progress,
            System.Threading.CancellationToken cancellationToken)
        {
            long processedBytes = 0;
            Report(progress, stage, 0, data.Length);

            while (processedBytes < data.Length)
            {
                int writeCount = (int)Math.Min(
                    BufferSize,
                    data.Length - processedBytes);

                await destination.WriteAsync(
                    data,
                    (int)processedBytes,
                    writeCount,
                    cancellationToken).ConfigureAwait(false);

                processedBytes += writeCount;
                Report(progress, stage, processedBytes, data.Length);
            }

            if (data.Length == 0)
                Report(progress, stage, 0, 0);
        }
    }
}
