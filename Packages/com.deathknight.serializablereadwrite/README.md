# Serializable Read Write

Unity 文件序列化、完整性校验、AES 加密和受保护 AssetBundle 加载工具。

## 安装

在 Unity Package Manager 中通过 Git URL 安装：

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.serializablereadwrite
```

所有公开 API 均位于 `SerializableReadWrite` 命名空间。

## 快速开始

```csharp
using SerializableReadWrite;

ObjectSaveRead.Save(path, data, "encryption-password", new HMACVerify());
MyData loaded = ObjectSaveRead.Read<MyData>(path, "encryption-password", new HMACVerify());
```

`passward`/`EncryptionPassword` 为 `null` 时不使用密码模式；`verify`/`Verify` 为 `null` 时不校验。密码模式下，AES Key 和 HMAC Key 会通过 PBKDF2 从同一个密码派生。已经持有随机 AES Key 时可使用 `*WithAes` API，跳过 PBKDF2；若同时使用 `HMACVerify`，还需提供独立随机的 `verifyKey`。

```csharp
using System.Security.Cryptography;

using Aes aes = Aes.Create();
aes.GenerateKey();

byte[] verifyKey = new byte[32];
using (RandomNumberGenerator random = RandomNumberGenerator.Create())
{
    random.GetBytes(verifyKey);
}

ObjectSaveRead.SaveWithAes(path, data, aes, new HMACVerify(), verifyKey);
MyData loaded = ObjectSaveRead.ReadWithAes<MyData>(
    path,
    aes,
    new HMACVerify(),
    verifyKey);
```

`*WithAes` 不会释放调用方的 AES，也不会修改它的 Key 或 IV。每次写入仍会生成新的随机 IV 并保存到文件，读取时自动取回该 IV。

## 公开 API

### ObjectSaveRead

使用 Newtonsoft.Json 在对象和受保护文件之间读写。适合存档、配置等可 JSON 序列化的数据。

```csharp
public static void Save<T>(
    string path,
    T data,
    string passward = null,
    IVerify verify = null);

public static T Read<T>(
    string path,
    string passward = null,
    IVerify verify = null);

public static void SaveWithAes<T>(
    string path,
    T data,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null);

public static T ReadWithAes<T>(
    string path,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null);
```

### ByteSaveRead

在原始 `byte[]` 和受保护文件之间读写。

```csharp
public static void Save(
    string path,
    byte[] data,
    string passward = null,
    IVerify verify = null);

public static byte[] Read(
    string path,
    string passward = null,
    IVerify verify = null);

public static void SaveWithAes(
    string path,
    byte[] data,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null);

public static byte[] ReadWithAes(
    string path,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null);
```

### FileEncrypt

流式处理普通文件，适合不希望把整个源文件一次性载入内存的场景。源路径与目标路径不能相同。

```csharp
public static void Encrypt(
    string sourcePath,
    string encryptedPath,
    string passward = null,
    IVerify verify = null,
    IProgress<FileEncryptProgress> progress = null);

public static void Decrypt(
    string encryptedPath,
    string outputPath,
    string passward = null,
    IVerify verify = null,
    IProgress<FileEncryptProgress> progress = null);

public static byte[] DecryptToBytes(
    string encryptedPath,
    string passward = null,
    IVerify verify = null,
    IProgress<FileEncryptProgress> progress = null);

public static void EncryptWithAes(
    string sourcePath,
    string encryptedPath,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null,
    IProgress<FileEncryptProgress> progress = null);

public static void DecryptWithAes(
    string encryptedPath,
    string outputPath,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null,
    IProgress<FileEncryptProgress> progress = null);

public static byte[] DecryptToBytesWithAes(
    string encryptedPath,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null,
    IProgress<FileEncryptProgress> progress = null);
```

进度示例：

```csharp
var progress = new Progress<FileEncryptProgress>(value =>
{
    UnityEngine.Debug.Log($"{value.Stage}: {value.TotalProgress:P0}");
});

FileEncrypt.Encrypt(sourcePath, protectedPath, "password", new HMACVerify(), progress);
```

### AssetBundleLoader

校验、解密并从内存加载受保护的 AssetBundle。文件可先通过 `FileEncrypt.Encrypt` 生成。

```csharp
public static AssetBundle Load(
    string protectedPath,
    string passward = null,
    IVerify verify = null,
    uint crc = 0);

public static Task<AssetBundle> LoadAsync(
    string protectedPath,
    string passward = null,
    IVerify verify = null,
    uint crc = 0);

public static AssetBundle LoadWithAes(
    string protectedPath,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null,
    uint crc = 0);

public static Task<AssetBundle> LoadAsyncWithAes(
    string protectedPath,
    Aes aes,
    IVerify verify = null,
    byte[] verifyKey = null,
    uint crc = 0);
```

`LoadAsync`/`LoadAsyncWithAes` 必须从 Unity 主线程调用；文件校验和解密在工作线程执行，AssetBundle 创建回到主线程。调用 `LoadAsyncWithAes` 后，任务完成前不能释放或并发使用该 AES。`crc` 为 `0` 时跳过 Unity 的 CRC 检查。

### ProtectedFile

底层流式 API。上层可自行决定明文内容的编码或序列化格式。

```csharp
public static void Write(
    string path,
    Action<Stream> writePlaintext,
    ProtectedFileOptions options = null,
    IProgress<FileEncryptProgress> progress = null,
    long plaintextLength = -1);

public static TResult Read<TResult>(
    string path,
    Func<Stream, TResult> readPlaintext,
    ProtectedFileOptions options = null,
    IProgress<FileEncryptProgress> progress = null);
```

启用 `Write` 进度报告时，必须通过 `plaintextLength` 提供明文总字节数。传给回调的流由 `ProtectedFile` 管理，回调不应关闭该流。

密码模式的文件布局为 `[Tag][salt][IV][data]`，直接 AES 模式为 `[Tag][IV][data]`。未启用的可选部分不会写入；启用 AES 时 `data` 为密文。Tag 校验范围包含 salt（若存在）、IV 和 data。

### ProtectedFileOptions

```csharp
public sealed class ProtectedFileOptions
{
    public string EncryptionPassword { get; set; }
    public Aes EncryptionAes { get; set; }
    public byte[] VerifyKey { get; set; }
    public IVerify Verify { get; set; }
}
```

- `EncryptionPassword`：`null` 表示不使用 AES 加密。
- `EncryptionAes`：直接使用调用方提供的 AES，跳过 PBKDF2；不能与 `EncryptionPassword` 同时设置，实例由调用方释放。
- `VerifyKey`：直接 AES 模式下传给校验器的 Key；底层使用副本，不修改调用方数组。
- `Verify`：`null` 表示不生成或验证 Tag。
- 使用 `HMACVerify` 时，PBKDF2-SHA256 从 `EncryptionPassword` 和随机 Salt 派生 64 字节，前 32 字节作为 AES Key，后 32 字节作为 HMAC Key。
- 直接 AES 模式使用 `HMACVerify` 时，必须另外提供 `VerifyKey`，不要复用 AES Key。

### IVerify

自定义完整性校验算法需实现此接口。`TagLength` 必须大于 `0`，`ComputeTag` 返回数组的长度必须与其一致。

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

流重载从流的当前位置处理到流末尾。

### HashVerify

无密钥 SHA-256 完整性校验，`TagLength` 为 32 字节。可发现文件损坏，但攻击者能够在修改内容后重新计算 Tag。

```csharp
public class HashVerify : IVerify
{
    public int TagLength { get; }

    public byte[] ComputeTag(byte[] data, byte[] passward = null);
    public byte[] ComputeTag(Stream data, byte[] passward = null);
    public bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null);
    public bool VerifyTag(Stream data, byte[] tag, byte[] passward = null);
}
```

### HMACVerify

基于密钥的 HMAC-SHA256 校验，`TagLength` 为 32 字节。密码模式下 HMAC Key 自动从 AES 密码派生；直接 AES 模式由调用方通过 `verifyKey`/`VerifyKey` 提供独立 Key。

```csharp
public class HMACVerify : IVerify
{
    public int TagLength { get; }

    public byte[] ComputeTag(byte[] data, byte[] passward);
    public byte[] ComputeTag(Stream data, byte[] passward = null);
    public bool VerifyTag(byte[] data, byte[] tag, byte[] passward = null);
    public bool VerifyTag(Stream data, byte[] tag, byte[] passward = null);
}
```

### FileEncryptProgress 与 FileEncryptStage

```csharp
public readonly struct FileEncryptProgress
{
    public FileEncryptStage Stage { get; }
    public long StageProcessedBytes { get; }
    public long StageTotalBytes { get; }
    public int CompletedStageCount { get; }
    public int TotalStageCount { get; }
    public bool IsStageCompleted { get; }
    public float StageProgress { get; }
    public float TotalProgress { get; }
}

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

`StageProgress` 和 `TotalProgress` 的范围为 0 到 1。并非每次操作都会经历所有阶段，实际阶段数由是否启用加密和校验决定。

## 校验方式选择

- `HashVerify`：仅用于检测意外损坏，不提供防篡改能力。
- `HMACVerify`：使用独立密钥验证 Salt（若存在）、IV 和密文，适合需要提高篡改成本的本地文件。
- `verify == null`：不写入也不检查 Tag。

## 安全边界

客户端中的密码和密钥可能被提取。本包可以发现意外损坏并提高篡改成本，但不能把不可信客户端变成可信环境。对于可由攻击者控制的文件，优先使用 `HMACVerify`，不要把无密钥哈希当作身份认证。

完整示例也可以从 Package Manager 导入 **Basic Usage**。
