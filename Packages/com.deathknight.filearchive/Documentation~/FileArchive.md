# FileArchive 使用文档

## 1. 功能说明

`FileArchive` 用于创建和解压 ZIP 文件，对外提供两个静态接口：

| 接口 | 用途 |
| --- | --- |
| `CompressFiles` | 将指定的多个文件压缩为 ZIP |
| `ExtractToDirectory` | 将 ZIP 中的文件解压到指定目录 |

命名空间：

```csharp
using SerializableReadWrite;
using System;
using System.IO.Compression;
using System.Threading.Tasks;
```

这些接口本身是同步接口。文件较大时，应使用 `await Task.Run(...)` 放到后台线程执行，避免阻塞 Unity 主线程。

---

## 2. 压缩指定文件

### 接口

```csharp
public static void CompressFiles(
    IEnumerable<string> sourceFiles,
    string archivePath,
    CompressionLevel compressionLevel = CompressionLevel.Optimal,
    IProgress<FileArchiveProgress> progress = null)
```

### 参数

| 参数 | 说明 |
| --- | --- |
| `sourceFiles` | 要压缩的文件路径集合 |
| `archivePath` | 输出 ZIP 文件路径 |
| `compressionLevel` | 压缩等级，默认是 `Optimal` |
| `progress` | 可选的进度回调 |

### 使用示例

```csharp
string archivePath = @"D:\Backup\SelectedFiles.zip";

string[] files =
{
    @"D:\GameData\config.json",
    @"D:\GameData\Save\player.dat"
};

await Task.Run(() =>
{
    FileArchive.CompressFiles(
        files,
        archivePath,
        CompressionLevel.Optimal);
});
```

注意：

- `sourceFiles` 不能为 `null`。
- 集合中的文件必须存在。
- ZIP 条目只使用源文件的文件名，不保留其所在目录。
- 不同目录中的同名文件会产生重复 ZIP 条目名，此时接口会拒绝压缩。
- `archivePath` 的父目录必须已经存在。
- 如果目标 ZIP 已存在，会被覆盖。

---

## 3. 解压 ZIP

### 接口

```csharp
public static void ExtractToDirectory(
    string archivePath,
    string outputDirectory,
    int maxFileCount = 4096,
    long maxTotalUncompressedBytes = 512L * 1024 * 1024,
    IProgress<FileArchiveProgress> progress = null)
```

### 参数

| 参数 | 说明 |
| --- | --- |
| `archivePath` | 要解压的 ZIP 文件路径 |
| `outputDirectory` | 解压输出目录 |
| `maxFileCount` | 允许解压的最大文件数量，默认 `4096` |
| `maxTotalUncompressedBytes` | 允许解压的最大总字节数，默认 `512 MB` |
| `progress` | 可选的进度回调 |

### 使用默认限制

```csharp
string archivePath = @"D:\Backup\GameData.zip";
string outputDirectory = @"D:\Restore\GameData";

await Task.Run(() =>
{
    FileArchive.ExtractToDirectory(
        archivePath,
        outputDirectory);
});
```

### 自定义解压限制

```csharp
int maxFileCount = 10000;
long maxBytes = 2L * 1024 * 1024 * 1024; // 2 GB

await Task.Run(() =>
{
    FileArchive.ExtractToDirectory(
        archivePath,
        outputDirectory,
        maxFileCount,
        maxBytes);
});
```

注意：

- ZIP 文件必须存在并且格式正确。
- 输出目录不存在时会自动创建。
- 输出目录中存在同名文件时会覆盖原文件。
- `maxFileCount` 必须大于 `0`。
- `maxTotalUncompressedBytes` 不能小于 `0`。
- 文件数量或解压总大小超过限制时，接口会拒绝解压。
- 接口会检查非法绝对路径和 `..` 上级目录，防止 ZIP 条目写到输出目录之外。
- ZIP 内存在重复文件条目时会拒绝解压。

---

## 4. 压缩等级

`compressionLevel` 使用 `System.IO.Compression.CompressionLevel`：

| 值 | 说明 |
| --- | --- |
| `CompressionLevel.Optimal` | 优先压缩率，默认选项 |
| `CompressionLevel.Fastest` | 优先速度，压缩率较低 |
| `CompressionLevel.NoCompression` | 只打包，不压缩文件内容 |

普通存档、配置和资源备份建议使用：

```csharp
CompressionLevel.Optimal
```

需要快速打包，或者文件本身已经压缩过时，可以使用：

```csharp
CompressionLevel.Fastest
```

---

## 5. 获取处理进度

创建 `Progress<FileArchiveProgress>`，然后传给压缩或解压接口：

```csharp
IProgress<FileArchiveProgress> progress =
    new Progress<FileArchiveProgress>(value =>
    {
        Debug.Log($"当前文件：{value.EntryName}");
        Debug.Log($"当前文件进度：{value.CurrentFileProgress:P0}");
        Debug.Log($"整体进度：{value.TotalProgress:P0}");
        Debug.Log($"文件数量：{value.CompletedFileCount}/{value.TotalFileCount}");
        Debug.Log($"处理字节：{value.TotalProcessedBytes}/{value.TotalBytes}");
    });

await Task.Run(() =>
{
    FileArchive.CompressFiles(
        sourceFiles,
        archivePath,
        CompressionLevel.Optimal,
        progress);
});
```

在 Unity 主线程中创建 `Progress<FileArchiveProgress>` 时，进度委托会返回 Unity 主线程执行，因此可以在回调中更新 `Slider`、`Text` 等 UI 控件。

不要在 `Task.Run` 内直接操作 Unity UI：

```csharp
// 错误：后台线程直接修改 Unity UI。
await Task.Run(() =>
{
    progressSlider.value = 0.5f;
});
```

应当通过进度回调更新：

```csharp
var progress = new Progress<FileArchiveProgress>(value =>
{
    progressSlider.value = value.TotalProgress;
    progressText.text = $"{value.TotalProgress:P0}";
});
```

---

## 6. FileArchiveProgress 字段

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `EntryName` | `string` | 当前处理的 ZIP 条目名称 |
| `CompletedFileCount` | `int` | 已完成的文件数量 |
| `TotalFileCount` | `int` | 文件总数量 |
| `CurrentFileProcessedBytes` | `long` | 当前文件已处理字节数 |
| `CurrentFileTotalBytes` | `long` | 当前文件总字节数 |
| `TotalProcessedBytes` | `long` | 全部文件已处理字节数 |
| `TotalBytes` | `long` | 全部文件总字节数 |
| `IsFileCompleted` | `bool` | 当前文件是否处理完成 |
| `CurrentFileProgress` | `float` | 当前文件进度，范围 `0-1` |
| `TotalProgress` | `float` | 整体进度，范围 `0-1` |

更新进度条时直接使用：

```csharp
currentFileSlider.value = value.CurrentFileProgress;
totalSlider.value = value.TotalProgress;
```

---

## 7. 完整调用示例

```csharp
using System;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace SerializableReadWrite
{
    public class ArchiveExample : MonoBehaviour
    {
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Text progressText;

        public async void BackupFiles()
        {
            string[] sourceFiles =
            {
                @"D:\GameData\config.json",
                @"D:\GameData\player.dat"
            };
            string archivePath = @"D:\Backup\GameData.zip";

            var progress = new Progress<FileArchiveProgress>(value =>
            {
                progressSlider.value = value.TotalProgress;
                progressText.text =
                    $"{value.CompletedFileCount}/{value.TotalFileCount}  " +
                    $"{value.TotalProgress:P0}";
            });

            try
            {
                await Task.Run(() =>
                {
                    FileArchive.CompressFiles(
                        sourceFiles,
                        archivePath,
                        CompressionLevel.Optimal,
                        progress);
                });

                Debug.Log("压缩完成");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
```

---

## 8. 异常处理

建议所有调用都使用 `try/catch`：

```csharp
try
{
    await Task.Run(() =>
    {
        FileArchive.ExtractToDirectory(
            archivePath,
            outputDirectory);
    });
}
catch (Exception exception)
{
    Debug.LogError($"文件归档操作失败：{exception.Message}");
}
```

常见异常：

| 异常 | 常见原因 |
| --- | --- |
| `ArgumentException` | 路径为空 |
| `ArgumentNullException` | `sourceFiles` 为 `null` |
| `ArgumentOutOfRangeException` | 解压数量或字节限制不合法 |
| `DirectoryNotFoundException` | 源目录不存在 |
| `FileNotFoundException` | 源文件或 ZIP 文件不存在 |
| `InvalidDataException` | ZIP 损坏、条目重复、路径非法或超过解压限制 |
| `IOException` | 文件被占用、没有写入权限或磁盘空间不足 |

调用前应确认：

1. 源目录和源文件存在。
2. ZIP 输出路径的父目录存在。
3. 程序对输出位置具有写入权限。
4. 目标文件没有被其他程序独占。
5. 解压限制符合实际 ZIP 的文件数量和总大小。
