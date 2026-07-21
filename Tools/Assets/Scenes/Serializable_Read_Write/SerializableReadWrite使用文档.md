# SerializableReadWrite 使用文档

## 1. 模块概览

命名空间：

```csharp
using SerializableReadWrite;
```

公开功能分为六层：

| 功能 | 公开类型 | 用途 |
| --- | --- | --- |
| 对象保存 | `ObjectSaveRead` | 对象与受保护 JSON 文件互转 |
| 字节保存 | `ByteSaveRead` | `byte[]` 与受保护文件互转 |
| 底层流读写 | `ProtectedFile`、`ProtectedFileOptions` | 自定义明文流的加密、校验、读写 |
| 普通文件转换 | `FileEncrypt` | 文件加密、解密及读取到内存 |
| 完整性校验 | `IVerify`、`HashVerify`、`HMACVerify` | SHA-256 或 HMAC-SHA256 校验 |
| ZIP 归档 | `FileArchive`、`FileArchiveProgress` | 目录/文件压缩和安全解压 |
| AssetBundle | `AssetBundleLoader` | 加载经过保护的 AssetBundle |

如果调用方使用独立的 `.asmdef`，需要在该 `.asmdef` 中添加对 `SerializableReadWrite` 的引用。

---

## 2. 通用参数约定

多个接口使用以下参数：

| 参数 | 含义 |
| --- | --- |
| `passward` | 加密密码；为 `null` 时不使用 AES 加密 |
| `verify` | 校验算法；为 `null` 时不写入也不验证校验码 |
| `verifyPassward` | 校验密钥；`HMACVerify` 必须提供，`HashVerify` 会忽略 |

推荐的保护组合：

```csharp
string encryptionPassword = "encryption-password";
IVerify verify = new HMACVerify();
string verifyPassword = "verify-password";
```

只检查文件是否损坏，不需要密钥：

```csharp
IVerify verify = new HashVerify();
```

写入和读取必须使用相同配置。文件自身不会记录“用了哪个校验器、是否加密、密码是什么”。

---

## 3. ObjectSaveRead

`ObjectSaveRead` 使用 JSON 序列化对象，然后交给 `ProtectedFile` 保存。

### 3.1 Save<T>

```csharp
public static void Save<T>(
    string path,
    T data,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null)
```

示例数据：

```csharp
[Serializable]
public class PlayerData
{
    public int level;
    public string playerName;
}
```

保存：

```csharp
var data = new PlayerData
{
    level = 12,
    playerName = "Player"
};

await Task.Run(() =>
{
    ObjectSaveRead.Save(
        savePath,
        data,
        "encryption-password",
        new HMACVerify(),
        "verify-password");
});
```

### 3.2 Read<T>

```csharp
public static T Read<T>(
    string path,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null)
```

读取：

```csharp
PlayerData data = await Task.Run(() =>
    ObjectSaveRead.Read<PlayerData>(
        savePath,
        "encryption-password",
        new HMACVerify(),
        "verify-password"));
```

注意：

- 文件使用 JSON 表示，类型字段需要能被 Newtonsoft.Json 序列化。
- 读取时的泛型类型必须与保存的数据结构兼容。
- 路径的父目录需要提前创建。
- 保存时会覆盖已有文件。

---

## 4. ByteSaveRead

用于直接保存和读取 `byte[]`。

### 4.1 Save

```csharp
public static void Save(
    string path,
    byte[] data,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null)
```

```csharp
byte[] bytes = File.ReadAllBytes(sourcePath);

await Task.Run(() =>
{
    ByteSaveRead.Save(
        targetPath,
        bytes,
        "encryption-password",
        new HMACVerify(),
        "verify-password");
});
```

### 4.2 Read

```csharp
public static byte[] Read(
    string path,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null)
```

```csharp
byte[] bytes = await Task.Run(() =>
    ByteSaveRead.Read(
        targetPath,
        "encryption-password",
        new HMACVerify(),
        "verify-password"));
```

注意：`Read` 会把完整明文读入内存。大文件优先使用 `ProtectedFile.Read` 或 `FileEncrypt.Decrypt` 的流式接口。

---

## 5. ProtectedFileOptions

```csharp
public sealed class ProtectedFileOptions
{
    public string EncryptionPassword { get; set; }
    public IVerify Verify { get; set; }
    public string VerifyKey { get; set; }
}
```

| 属性 | 说明 |
| --- | --- |
| `EncryptionPassword` | 非空时使用 AES-256 加密；`null` 表示不加密 |
| `Verify` | 校验实现；`null` 表示不校验 |
| `VerifyKey` | 校验密钥字符串，内部按 UTF-8 转成字节 |

示例：

```csharp
var options = new ProtectedFileOptions
{
    EncryptionPassword = "encryption-password",
    Verify = new HMACVerify(),
    VerifyKey = "verify-password"
};
```

支持四种组合：

| 加密密码 | 校验器 | 效果 |
| --- | --- | --- |
| `null` | `null` | 普通明文文件 |
| 非空 | `null` | 仅 AES 加密 |
| `null` | 非空 | 明文加校验码 |
| 非空 | 非空 | AES 加密并校验密文 |

---

## 6. ProtectedFile

`ProtectedFile` 是底层流式接口。调用者决定如何向明文流写数据、如何从明文流解析数据。

### 6.1 Write

```csharp
public static void Write(
    string path,
    Action<Stream> writePlaintext,
    ProtectedFileOptions options = null,
    IProgress<FileEncryptProgress> progress = null,
    long plaintextLength = -1)
```

写入文本：

```csharp
var options = new ProtectedFileOptions
{
    EncryptionPassword = "encryption-password",
    Verify = new HMACVerify(),
    VerifyKey = "verify-password"
};

string content = "hello";
long byteLength = Encoding.UTF8.GetByteCount(content);

await Task.Run(() =>
{
    ProtectedFile.Write(
        path,
        stream =>
        {
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(false),
                16 * 1024,
                true);
            writer.Write(content);
            writer.Flush();
        },
        options,
        progress,
        byteLength);
});
```

参数说明：

| 参数 | 说明 |
| --- | --- |
| `path` | 输出文件路径 |
| `writePlaintext` | 接收明文输出流的写入委托 |
| `options` | 加密和校验配置；`null` 等于空配置 |
| `progress` | 加密/校验进度回调 |
| `plaintextLength` | 明文总字节数，仅启用进度时必须提供 |

启用 `progress` 时，如果 `plaintextLength < 0`，接口会抛出异常。

### 6.2 Read<TResult>

```csharp
public static TResult Read<TResult>(
    string path,
    Func<Stream, TResult> readPlaintext,
    ProtectedFileOptions options = null,
    IProgress<FileEncryptProgress> progress = null)
```

读取文本：

```csharp
string content = await Task.Run(() =>
    ProtectedFile.Read(
        path,
        stream =>
        {
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                false,
                16 * 1024,
                true);
            return reader.ReadToEnd();
        },
        options,
        progress));
```

`readPlaintext` 的返回值就是 `Read<TResult>` 的返回值，可以返回字符串、对象、统计结果或其他解析结果。

### 6.3 流的所有权

传入委托的明文流由 `ProtectedFile` 管理。上层包装 `StreamReader`、`StreamWriter` 时，应使用 `leaveOpen: true`，不要提前关闭底层流。

---

## 7. FileEncrypt

`FileEncrypt` 用于普通文件与受保护文件之间的转换，全程采用流式复制。

### 7.1 Encrypt

```csharp
public static void Encrypt(
    string sourcePath,
    string encryptedPath,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null,
    IProgress<FileEncryptProgress> progress = null)
```

```csharp
await Task.Run(() =>
{
    FileEncrypt.Encrypt(
        sourcePath,
        encryptedPath,
        "encryption-password",
        new HMACVerify(),
        "verify-password",
        progress);
});
```

### 7.2 Decrypt

```csharp
public static void Decrypt(
    string encryptedPath,
    string outputPath,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null,
    IProgress<FileEncryptProgress> progress = null)
```

```csharp
await Task.Run(() =>
{
    FileEncrypt.Decrypt(
        encryptedPath,
        outputPath,
        "encryption-password",
        new HMACVerify(),
        "verify-password",
        progress);
});
```

### 7.3 DecryptToBytes

```csharp
public static byte[] DecryptToBytes(
    string encryptedPath,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null,
    IProgress<FileEncryptProgress> progress = null)
```

```csharp
byte[] bytes = await Task.Run(() =>
    FileEncrypt.DecryptToBytes(
        encryptedPath,
        "encryption-password",
        new HMACVerify(),
        "verify-password",
        progress));
```

注意：

- 输入路径和输出路径不能相同。
- 输出文件已存在时会被覆盖。
- 输出路径的父目录必须存在。
- `DecryptToBytes` 会将完整明文保存在内存中，只适合可控大小的文件。

---

## 8. FileEncryptProgress

### 8.1 FileEncryptStage

```csharp
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
```

| 阶段 | 说明 |
| --- | --- |
| `Writing` | 不加密时写入明文 |
| `Encrypting` | AES 加密写入 |
| `GeneratingTag` | 生成校验码 |
| `VerifyingTag` | 验证校验码 |
| `Reading` | 不加密时读取明文 |
| `Decrypting` | AES 解密读取 |
| `Completed` | 全部阶段完成 |

### 8.2 进度属性

| 属性 | 说明 |
| --- | --- |
| `Stage` | 当前阶段 |
| `StageProcessedBytes` | 当前阶段已处理字节数 |
| `StageTotalBytes` | 当前阶段总字节数 |
| `CompletedStageCount` | 已完成阶段数 |
| `TotalStageCount` | 总阶段数 |
| `IsStageCompleted` | 当前阶段是否完成 |
| `StageProgress` | 当前阶段进度，范围 `0-1` |
| `TotalProgress` | 整体进度，范围 `0-1` |

Unity UI 进度示例：

```csharp
var progress = new Progress<FileEncryptProgress>(value =>
{
    progressSlider.value = value.TotalProgress;
    progressText.text = $"{value.Stage}  {value.TotalProgress:P0}";
});
```

应在 Unity 主线程创建 `Progress<T>`，再把文件函数放进 `Task.Run`。这样回调会返回主线程，可以安全更新 Unity UI。

---

## 9. IVerify

自定义校验器需要实现：

```csharp
public interface IVerify
{
    int TagLength { get; }

    byte[] ComputeTag(byte[] data, byte[] passward = null);
    bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null);

    byte[] ComputeTag(Stream data, byte[] passward = null);
    bool VerifyTag(Stream data, byte[] tag, byte[] passward = null);
}
```

| 成员 | 说明 |
| --- | --- |
| `TagLength` | 校验码固定字节数 |
| `ComputeTag(byte[])` | 根据字节数组计算校验码 |
| `VerifyTag(byte[])` | 验证字节数组与校验码 |
| `ComputeTag(Stream)` | 从流当前位置到结尾计算校验码 |
| `VerifyTag(Stream)` | 验证流数据与校验码 |

流式版本适合大文件，避免把完整文件读入内存。

---

## 10. HashVerify

`HashVerify` 使用 SHA-256，`TagLength` 固定为 32 字节。

```csharp
var verify = new HashVerify();
byte[] tag = verify.ComputeTag(data);
bool valid = verify.VerifyTag(data, tag);
```

特点：

- 能检测意外损坏。
- 不使用密钥，`passward` 参数会被忽略。
- 攻击者修改数据后也能重新计算 SHA-256，因此不能防止主动篡改。

---

## 11. HMACVerify

`HMACVerify` 使用 HMAC-SHA256，`TagLength` 固定为 32 字节。

```csharp
var verify = new HMACVerify();
byte[] key = Encoding.UTF8.GetBytes("verify-password");

byte[] tag = verify.ComputeTag(data, key);
bool valid = verify.VerifyTag(data, tag, key);
```

特点：

- 校验结果同时依赖文件内容和密钥。
- 不知道密钥的一方无法在修改文件后生成有效校验码。
- `passward`/校验密钥不能为 `null`。
- 写入和读取必须使用同一个密钥。

业务层通常不需要直接转成 `byte[]`，可通过 `verifyPassward` 或 `ProtectedFileOptions.VerifyKey` 传入字符串。

---

## 12. FileArchive

### 12.1 CompressDirectory

```csharp
public static void CompressDirectory(
    string sourceDirectory,
    string archivePath,
    CompressionLevel compressionLevel = CompressionLevel.Optimal,
    IProgress<FileArchiveProgress> progress = null)
```

```csharp
await Task.Run(() =>
    FileArchive.CompressDirectory(
        sourceDirectory,
        archivePath,
        CompressionLevel.Optimal,
        archiveProgress));
```

压缩源目录中的全部文件，并在 ZIP 中保留相对目录结构。空目录不会单独写入 ZIP。

### 12.2 CompressFiles

```csharp
public static void CompressFiles(
    string baseDirectory,
    IEnumerable<string> sourceFiles,
    string archivePath,
    CompressionLevel compressionLevel = CompressionLevel.Optimal,
    IProgress<FileArchiveProgress> progress = null)
```

```csharp
string[] files =
{
    Path.Combine(baseDirectory, "config.json"),
    Path.Combine(baseDirectory, "Save", "player.dat")
};

await Task.Run(() =>
    FileArchive.CompressFiles(
        baseDirectory,
        files,
        archivePath,
        CompressionLevel.Optimal,
        archiveProgress));
```

所有源文件必须位于 `baseDirectory` 内。ZIP 条目名使用文件相对于该目录的路径。

### 12.3 ExtractToDirectory

```csharp
public static void ExtractToDirectory(
    string archivePath,
    string outputDirectory,
    int maxFileCount = 4096,
    long maxTotalUncompressedBytes = 512L * 1024 * 1024,
    IProgress<FileArchiveProgress> progress = null)
```

```csharp
await Task.Run(() =>
    FileArchive.ExtractToDirectory(
        archivePath,
        outputDirectory,
        4096,
        512L * 1024 * 1024,
        archiveProgress));
```

解压保护：

- 限制最大文件数量。
- 限制解压后的最大总字节数。
- 拒绝绝对路径和 `..` 上级目录。
- 拒绝写到输出目录之外。
- 拒绝重复文件条目。

注意：ZIP 输出路径的父目录必须存在；解压输出目录可以不存在，接口会自动创建。

---

## 13. FileArchiveProgress

| 属性 | 说明 |
| --- | --- |
| `EntryName` | 当前 ZIP 条目名 |
| `CompletedFileCount` | 已完成文件数 |
| `TotalFileCount` | 文件总数 |
| `CurrentFileProcessedBytes` | 当前文件已处理字节数 |
| `CurrentFileTotalBytes` | 当前文件总字节数 |
| `TotalProcessedBytes` | 整体已处理字节数 |
| `TotalBytes` | 整体总字节数 |
| `IsFileCompleted` | 当前文件是否完成 |
| `CurrentFileProgress` | 当前文件进度，范围 `0-1` |
| `TotalProgress` | 整体进度，范围 `0-1` |

```csharp
var archiveProgress = new Progress<FileArchiveProgress>(value =>
{
    currentFileSlider.value = value.CurrentFileProgress;
    totalSlider.value = value.TotalProgress;
    statusText.text =
        $"{value.CompletedFileCount}/{value.TotalFileCount} " +
        $"{value.EntryName}";
});
```

更完整的 ZIP 参数说明见 [FileArchive使用文档.md](FileArchive使用文档.md)。

---

## 14. AssetBundleLoader

`AssetBundleLoader` 读取由 `FileEncrypt.Encrypt` 生成的受保护 AssetBundle 文件。

### 14.1 Load

```csharp
public static AssetBundle Load(
    string protectedPath,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null,
    uint crc = 0)
```

```csharp
AssetBundle bundle = AssetBundleLoader.Load(
    protectedBundlePath,
    "encryption-password",
    new HMACVerify(),
    "verify-password",
    crc: 0);
```

该接口会同步完成读取、校验、解密和 `AssetBundle.LoadFromMemory`。大文件会阻塞调用线程。

### 14.2 LoadAsync

```csharp
public static Task<AssetBundle> LoadAsync(
    string protectedPath,
    string passward = null,
    IVerify verify = null,
    string verifyPassward = null,
    uint crc = 0)
```

```csharp
AssetBundle bundle = await AssetBundleLoader.LoadAsync(
    protectedBundlePath,
    "encryption-password",
    new HMACVerify(),
    "verify-password",
    crc: 0);
```

`LoadAsync` 的文件读取、校验和解密在工作线程执行，AssetBundle 创建会回到 Unity 主线程异步执行。

重要限制：

- `LoadAsync` 必须从 Unity 主线程调用。
- `crc` 为 `0` 时不进行 Unity AssetBundle CRC 检查。
- 返回 `null` 不会作为正常结果；无效 Bundle 会抛出 `InvalidDataException`。
- 使用完成后由调用方执行 `bundle.Unload(...)`。

### 14.3 创建受保护 AssetBundle

```csharp
await Task.Run(() =>
{
    FileEncrypt.Encrypt(
        originalBundlePath,
        protectedBundlePath,
        "encryption-password",
        new HMACVerify(),
        "verify-password");
});
```

随后使用相同参数调用 `AssetBundleLoader.Load` 或 `LoadAsync`。

---

## 15. 统一异步调用方式

除 `AssetBundleLoader.LoadAsync` 外，其余文件接口都是同步接口。Unity 中推荐：

```csharp
public async void ExecuteFileOperation()
{
    var progress = new Progress<FileEncryptProgress>(value =>
    {
        progressSlider.value = value.TotalProgress;
    });

    try
    {
        await Task.Run(() =>
        {
            FileEncrypt.Encrypt(
                sourcePath,
                targetPath,
                "encryption-password",
                new HMACVerify(),
                "verify-password",
                progress);
        });

        Debug.Log("操作完成");
    }
    catch (Exception exception)
    {
        Debug.LogException(exception);
    }
}
```

规则：

1. 在 Unity 主线程读取 `InputField.text` 等参数。
2. 在 Unity 主线程创建 `Progress<T>`。
3. 只把纯文件操作放进 `Task.Run`。
4. 不要在 `Task.Run` 内直接访问 Unity 对象。
5. `await` 返回主线程后再更新最终状态。

---

## 16. 常见异常

| 异常 | 常见原因 |
| --- | --- |
| `ArgumentException` | 路径为空、输入输出路径相同、文件不在基础目录内 |
| `ArgumentNullException` | 数据、委托、文件集合或 HMAC 密钥为空 |
| `ArgumentOutOfRangeException` | 进度缺少明文长度、解压限制不合法 |
| `DirectoryNotFoundException` | 源目录或目标父目录不存在 |
| `FileNotFoundException` | 源文件、受保护文件或 ZIP 不存在 |
| `InvalidDataException` | 密码/校验不匹配、文件被修改、ZIP 非法、AssetBundle 无效 |
| `CryptographicException` | AES 密码错误、密文损坏或填充无效 |
| `IOException` | 文件被占用、权限不足、磁盘空间不足 |

建议所有文件操作都使用 `try/catch`，并把异常信息显示到日志或界面状态中。

---

## 17. 安全边界

- `HashVerify` 只能检测损坏，不能防止主动篡改。
- `HMACVerify` 能验证持有密钥的一方生成的数据，优先用于需要防篡改的场景。
- 客户端程序中的密码和密钥最终可能被提取，因此客户端加密不能替代服务端权限校验。
- AES 用于隐藏文件内容，HMAC 用于验证内容是否被修改，两者职责不同。
- 读取受保护文件时，必须使用与写入时相同的加密、校验配置和密钥。
