# File Archive

面向 Unity 的 ZIP 压缩、安全解压和进度报告工具。

## 安装

在 Unity Package Manager 中通过 Git URL 安装：

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.filearchive
```

## 快速开始

```csharp
using SerializableReadWrite;

FileArchive.CompressFiles(sourceFiles, archivePath);
FileArchive.ExtractToDirectory(archivePath, outputDirectory);
```

解压接口默认限制文件数量和总解压大小，并检查目标路径，降低 Zip Slip 和异常压缩包风险。
