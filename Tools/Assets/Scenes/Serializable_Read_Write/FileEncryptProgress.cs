using System;
using System.Diagnostics;

namespace SerializableReadWrite
{
    public enum FileEncryptStage
    {
        Writing,
        Encrypting,
        GeneratingTag,
        VerifyingTag,
        Reading,
        Decrypting,
        Completed
    }

    /// <summary>
    /// 文件加密、解密和校验过程中的进度信息。
    /// </summary>
    public readonly struct FileEncryptProgress
    {
        public FileEncryptStage Stage { get; }

        public long StageProcessedBytes { get; }

        public long StageTotalBytes { get; }

        public int CompletedStageCount { get; }

        public int TotalStageCount { get; }

        public bool IsStageCompleted { get; }

        public float StageProgress => StageTotalBytes == 0
            ? IsStageCompleted ? 1f : 0f
            : (float)StageProcessedBytes / StageTotalBytes;

        public float TotalProgress => Stage == FileEncryptStage.Completed
            ? 1f
            : (CompletedStageCount + StageProgress) / TotalStageCount;

        internal FileEncryptProgress(
            FileEncryptStage stage,
            long stageProcessedBytes,
            long stageTotalBytes,
            int completedStageCount,
            int totalStageCount,
            bool isStageCompleted)
        {
            Stage = stage;
            StageProcessedBytes = stageProcessedBytes;
            StageTotalBytes = stageTotalBytes;
            CompletedStageCount = completedStageCount;
            TotalStageCount = totalStageCount;
            IsStageCompleted = isStageCompleted;
        }
    }

    internal sealed class FileEncryptProgressReporter
    {
        private const long ProgressIntervalBytes = 1024 * 1024;
        private const int ProgressIntervalMilliseconds = 100;

        private readonly IProgress<FileEncryptProgress> progress;
        private readonly Stopwatch stopwatch = new Stopwatch();

        private FileEncryptStage stage;
        private long stageProcessedBytes;
        private long stageTotalBytes;
        private long bytesSinceLastReport;
        private int completedStageCount;

        public int TotalStageCount { get; }

        public FileEncryptProgressReporter(
            IProgress<FileEncryptProgress> progress,
            int totalStageCount)
        {
            this.progress = progress ?? throw new ArgumentNullException(nameof(progress));

            if (totalStageCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(totalStageCount));

            TotalStageCount = totalStageCount;
        }

        public void BeginStage(FileEncryptStage stage, long totalBytes)
        {
            this.stage = stage;
            stageProcessedBytes = 0;
            stageTotalBytes = Math.Max(0, totalBytes);
            bytesSinceLastReport = 0;
            stopwatch.Restart();
            Report(false);
        }

        public void AddBytes(int count)
        {
            if (count <= 0)
                return;

            stageProcessedBytes += count;
            bytesSinceLastReport += count;

            if (bytesSinceLastReport >= ProgressIntervalBytes ||
                stopwatch.ElapsedMilliseconds >= ProgressIntervalMilliseconds)
            {
                Report(false);
                bytesSinceLastReport = 0;
                stopwatch.Restart();
            }
        }

        public void CompleteStage()
        {
            stageProcessedBytes = stageTotalBytes;
            Report(true);
            completedStageCount++;
            bytesSinceLastReport = 0;
            stopwatch.Reset();
        }

        public void Complete()
        {
            progress.Report(new FileEncryptProgress(
                FileEncryptStage.Completed,
                0,
                0,
                TotalStageCount,
                TotalStageCount,
                true));
        }

        private void Report(bool isStageCompleted)
        {
            progress.Report(new FileEncryptProgress(
                stage,
                stageProcessedBytes,
                stageTotalBytes,
                completedStageCount,
                TotalStageCount,
                isStageCompleted));
        }
    }
}
