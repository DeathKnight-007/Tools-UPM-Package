using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace SerializableReadWrite
{
    /// <summary>
    /// 负责把多个文件压缩成一个 ZIP 文件，以及把 ZIP 中的文件解压到目录。
    /// </summary>
    public static class FileArchive
    {
        private const int FileBufferSize = 1024 * 64;
        private const int DefaultMaxFileCount = 4096;
        private const long DefaultMaxTotalUncompressedBytes = 512L * 1024 * 1024;
        private const long ProgressIntervalBytes = 1024 * 1024;
        private const int ProgressIntervalMilliseconds = 100;

        /// <summary>
        /// 把目录中的全部文件压缩成一个 ZIP 文件，ZIP 中保留相对目录结构。
        /// </summary>
        public static void CompressDirectory(
            string sourceDirectory,
            string archivePath,
            CompressionLevel compressionLevel = CompressionLevel.Optimal,
            IProgress<FileArchiveProgress> progress = null)
        {
            if (string.IsNullOrEmpty(sourceDirectory))
                throw new ArgumentException("源目录路径不能为空", nameof(sourceDirectory));

            string fullSourceDirectory = Path.GetFullPath(sourceDirectory);

            if (!Directory.Exists(fullSourceDirectory))
                throw new DirectoryNotFoundException($"源目录不存在：{fullSourceDirectory}");

            string[] sourceFiles = Directory.GetFiles(
                fullSourceDirectory,
                "*",
                SearchOption.AllDirectories);

            CompressFiles(
                fullSourceDirectory,
                sourceFiles,
                archivePath,
                compressionLevel,
                progress);
        }

        /// <summary>
        /// 把指定的多个文件压缩成一个 ZIP 文件。
        /// ZIP 中的文件名使用文件相对于 baseDirectory 的路径。
        /// </summary>
        public static void CompressFiles(
            string baseDirectory,
            IEnumerable<string> sourceFiles,
            string archivePath,
            CompressionLevel compressionLevel = CompressionLevel.Optimal,
            IProgress<FileArchiveProgress> progress = null)
        {
            if (string.IsNullOrEmpty(baseDirectory))
                throw new ArgumentException("基础目录路径不能为空", nameof(baseDirectory));

            if (sourceFiles == null)
                throw new ArgumentNullException(nameof(sourceFiles));

            if (string.IsNullOrEmpty(archivePath))
                throw new ArgumentException("压缩文件路径不能为空", nameof(archivePath));

            string fullBaseDirectory = Path.GetFullPath(baseDirectory);

            if (!Directory.Exists(fullBaseDirectory))
                throw new DirectoryNotFoundException($"基础目录不存在：{fullBaseDirectory}");

            string fullArchivePath = Path.GetFullPath(archivePath);
            StringComparison pathComparison = GetPathComparison();
            var files = new List<ArchiveSourceFile>();
            var entryNames = new HashSet<string>(StringComparer.Ordinal);
            long totalBytes = 0;

            // 先固定文件清单，再创建目标文件，防止目标 ZIP 位于源目录时把自己压进去。
            foreach (string sourceFile in sourceFiles)
            {
                if (string.IsNullOrEmpty(sourceFile))
                    throw new ArgumentException("源文件路径不能为空", nameof(sourceFiles));

                string fullSourcePath = Path.GetFullPath(sourceFile);

                if (string.Equals(fullSourcePath, fullArchivePath, pathComparison))
                    continue;

                if (!File.Exists(fullSourcePath))
                    throw new FileNotFoundException("源文件不存在", fullSourcePath);

                string entryName = GetEntryName(
                    fullBaseDirectory,
                    fullSourcePath,
                    pathComparison);

                if (!entryNames.Add(entryName))
                    throw new InvalidDataException($"ZIP中存在重复文件名：{entryName}");

                long fileLength = new FileInfo(fullSourcePath).Length;

                if (fileLength > long.MaxValue - totalBytes)
                    throw new InvalidDataException("源文件总大小超过支持范围");

                totalBytes += fileLength;
                files.Add(new ArchiveSourceFile(fullSourcePath, entryName, fileLength));
            }

            using FileStream archiveStream = new FileStream(
                fullArchivePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                FileBufferSize);

            using var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Create,
                false);

            int completedFileCount = 0;
            long totalProcessedBytes = 0;
            byte[] copyBuffer = new byte[FileBufferSize];

            foreach (ArchiveSourceFile file in files)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    file.EntryName,
                    compressionLevel);

                using (Stream entryStream = entry.Open())
                using (FileStream sourceStream = new FileStream(
                    file.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    FileBufferSize))
                {
                    if (sourceStream.Length != file.Length)
                        throw new IOException($"压缩过程中源文件大小发生变化：{file.FullPath}");

                    CopyWithProgress(
                        sourceStream,
                        entryStream,
                        copyBuffer,
                        file.EntryName,
                        file.Length,
                        completedFileCount,
                        files.Count,
                        ref totalProcessedBytes,
                        totalBytes,
                        progress);
                }

                completedFileCount++;
                ReportProgress(
                    progress,
                    file.EntryName,
                    completedFileCount,
                    files.Count,
                    file.Length,
                    file.Length,
                    totalProcessedBytes,
                    totalBytes,
                    true);
            }
        }

        /// <summary>
        /// 把 ZIP 中的全部文件解压到指定目录。
        /// 默认限制用于避免异常压缩包写入过多文件或占用过多硬盘空间。
        /// </summary>
        public static void ExtractToDirectory(
            string archivePath,
            string outputDirectory,
            int maxFileCount = DefaultMaxFileCount,
            long maxTotalUncompressedBytes = DefaultMaxTotalUncompressedBytes,
            IProgress<FileArchiveProgress> progress = null)
        {
            if (string.IsNullOrEmpty(archivePath))
                throw new ArgumentException("压缩文件路径不能为空", nameof(archivePath));

            if (string.IsNullOrEmpty(outputDirectory))
                throw new ArgumentException("输出目录路径不能为空", nameof(outputDirectory));

            if (maxFileCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxFileCount), "最大文件数量必须大于0");

            if (maxTotalUncompressedBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxTotalUncompressedBytes),
                    "最大解压字节数不能小于0");
            }

            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            string outputDirectoryPrefix = fullOutputDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            StringComparison pathComparison = GetPathComparison();

            Directory.CreateDirectory(fullOutputDirectory);

            using FileStream archiveStream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                FileBufferSize);

            using var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Read,
                false);

            var extractedFiles = new HashSet<string>(StringComparer.Ordinal);
            int totalFileCount = 0;
            long totalBytes = 0;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    totalFileCount++;

                    if (entry.Length > maxTotalUncompressedBytes - totalBytes)
                    {
                        throw new InvalidDataException(
                            $"ZIP解压后的总大小超过限制：{maxTotalUncompressedBytes} byte");
                    }

                    totalBytes += entry.Length;
                }
            }

            if (totalFileCount > maxFileCount)
                throw new InvalidDataException($"ZIP中的文件数量超过限制：{maxFileCount}");

            long totalProcessedBytes = 0;
            byte[] copyBuffer = new byte[FileBufferSize];

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string entryName = NormalizeAndValidateEntryName(entry.FullName);
                string destinationPath = Path.GetFullPath(Path.Combine(
                    fullOutputDirectory,
                    entryName.Replace('/', Path.DirectorySeparatorChar)));

                if (!destinationPath.StartsWith(outputDirectoryPrefix, pathComparison))
                {
                    throw new InvalidDataException(
                        $"ZIP条目试图写到输出目录之外：{entry.FullName}");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                if (extractedFiles.Count >= maxFileCount)
                    throw new InvalidDataException($"ZIP中的文件数量超过限制：{maxFileCount}");

                if (!extractedFiles.Add(entryName))
                    throw new InvalidDataException($"ZIP中存在重复文件名：{entryName}");

                string destinationDirectory = Path.GetDirectoryName(destinationPath);

                if (!string.IsNullOrEmpty(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                using (Stream entryStream = entry.Open())
                using (FileStream outputStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    FileBufferSize))
                {
                    CopyWithProgress(
                        entryStream,
                        outputStream,
                        copyBuffer,
                        entryName,
                        entry.Length,
                        extractedFiles.Count - 1,
                        totalFileCount,
                        ref totalProcessedBytes,
                        totalBytes,
                        progress);
                }

                ReportProgress(
                    progress,
                    entryName,
                    extractedFiles.Count,
                    totalFileCount,
                    entry.Length,
                    entry.Length,
                    totalProcessedBytes,
                    totalBytes,
                    true);
            }
        }

        private static void CopyWithProgress(
            Stream input,
            Stream output,
            byte[] buffer,
            string entryName,
            long currentFileTotalBytes,
            int completedFileCount,
            int totalFileCount,
            ref long totalProcessedBytes,
            long totalBytes,
            IProgress<FileArchiveProgress> progress)
        {
            long currentFileProcessedBytes = 0;
            long bytesSinceLastReport = 0;
            var reportStopwatch = Stopwatch.StartNew();

            ReportProgress(
                progress,
                entryName,
                completedFileCount,
                totalFileCount,
                0,
                currentFileTotalBytes,
                totalProcessedBytes,
                totalBytes,
                false);

            while (true)
            {
                int readCount = input.Read(buffer, 0, buffer.Length);

                if (readCount == 0)
                    break;

                output.Write(buffer, 0, readCount);

                currentFileProcessedBytes += readCount;
                totalProcessedBytes += readCount;
                bytesSinceLastReport += readCount;

                if (bytesSinceLastReport >= ProgressIntervalBytes ||
                    reportStopwatch.ElapsedMilliseconds >= ProgressIntervalMilliseconds)
                {
                    ReportProgress(
                        progress,
                        entryName,
                        completedFileCount,
                        totalFileCount,
                        currentFileProcessedBytes,
                        currentFileTotalBytes,
                        totalProcessedBytes,
                        totalBytes,
                        false);

                    bytesSinceLastReport = 0;
                    reportStopwatch.Restart();
                }
            }

            if (currentFileProcessedBytes != currentFileTotalBytes)
            {
                throw new InvalidDataException(
                    $"文件实际读取大小与记录大小不一致：{entryName}");
            }
        }

        private static void ReportProgress(
            IProgress<FileArchiveProgress> progress,
            string entryName,
            int completedFileCount,
            int totalFileCount,
            long currentFileProcessedBytes,
            long currentFileTotalBytes,
            long totalProcessedBytes,
            long totalBytes,
            bool isFileCompleted)
        {
            progress?.Report(new FileArchiveProgress(
                entryName,
                completedFileCount,
                totalFileCount,
                currentFileProcessedBytes,
                currentFileTotalBytes,
                totalProcessedBytes,
                totalBytes,
                isFileCompleted));
        }

        private static string GetEntryName(
            string fullBaseDirectory,
            string fullSourcePath,
            StringComparison pathComparison)
        {
            string baseDirectoryPrefix = fullBaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullSourcePath.StartsWith(baseDirectoryPrefix, pathComparison))
            {
                throw new ArgumentException(
                    $"源文件不在基础目录内：{fullSourcePath}",
                    nameof(fullSourcePath));
            }

            return fullSourcePath
                .Substring(baseDirectoryPrefix.Length)
                .Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string NormalizeAndValidateEntryName(string entryName)
        {
            string normalizedName = entryName.Replace('\\', '/');
            string[] pathParts = normalizedName.Split('/');

            if (normalizedName.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidDataException($"ZIP包含非法绝对路径：{entryName}");

            foreach (string pathPart in pathParts)
            {
                if (pathPart == "..")
                    throw new InvalidDataException($"ZIP包含非法上级目录路径：{entryName}");
            }

            return normalizedName;
        }

        private static StringComparison GetPathComparison()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private readonly struct ArchiveSourceFile
        {
            public string FullPath { get; }

            public string EntryName { get; }

            public long Length { get; }

            public ArchiveSourceFile(string fullPath, string entryName, long length)
            {
                FullPath = fullPath;
                EntryName = entryName;
                Length = length;
            }
        }
    }
}
