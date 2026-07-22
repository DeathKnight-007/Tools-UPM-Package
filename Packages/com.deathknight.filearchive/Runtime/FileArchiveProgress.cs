namespace SerializableReadWrite
{
    /// <summary>
    /// 文件压缩或解压过程中的进度信息。
    /// </summary>
    public readonly struct FileArchiveProgress
    {
        public string EntryName { get; }

        public int CompletedFileCount { get; }

        public int TotalFileCount { get; }

        public long CurrentFileProcessedBytes { get; }

        public long CurrentFileTotalBytes { get; }

        public long TotalProcessedBytes { get; }

        public long TotalBytes { get; }

        public bool IsFileCompleted { get; }

        public float CurrentFileProgress => CurrentFileTotalBytes == 0
            ? IsFileCompleted ? 1f : 0f
            : (float)CurrentFileProcessedBytes / CurrentFileTotalBytes;

        public float TotalProgress => TotalBytes == 0
            ? 1f
            : (float)TotalProcessedBytes / TotalBytes;

        internal FileArchiveProgress(
            string entryName,
            int completedFileCount,
            int totalFileCount,
            long currentFileProcessedBytes,
            long currentFileTotalBytes,
            long totalProcessedBytes,
            long totalBytes,
            bool isFileCompleted)
        {
            EntryName = entryName;
            CompletedFileCount = completedFileCount;
            TotalFileCount = totalFileCount;
            CurrentFileProcessedBytes = currentFileProcessedBytes;
            CurrentFileTotalBytes = currentFileTotalBytes;
            TotalProcessedBytes = totalProcessedBytes;
            TotalBytes = totalBytes;
            IsFileCompleted = isFileCompleted;
        }
    }
}
