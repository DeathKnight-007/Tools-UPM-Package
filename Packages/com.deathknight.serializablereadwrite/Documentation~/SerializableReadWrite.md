# SerializableReadWrite 使用文档

## 1. 通用约定

所有 API 位于：

```csharp
using SerializableReadWrite;
```

常用参数：

| 参数 | 含义 |
| --- | --- |
| `passward` | AES 密码；为 `null` 时不加密 |
| `aes` | 调用方持有的 AES；使用该模式时跳过 PBKDF2 |
| `verifyKey` | 直接 AES 模式下传给校验器的独立 Key |
| `verify` | 校验算法；为 `null` 时不生成或验证 Tag |

使用 `HMACVerify` 时必须提供非空 AES 密码，但不再需要额外的 HMAC 密码。内部通过 PBKDF2-SHA256 派生 64 字节密钥材料：

```text
Password + Salt
       ↓ PBKDF2-SHA256（100000 次）
前 32 字节 -> AES-256 Key
后 32 字节 -> HMAC-SHA256 Key
```

HMAC 覆盖 `Salt + IV + 密文`，读取时先验证 HMAC，通过后才执行 AES 解密。

若调用方已经持有随机 AES Key，可使用 `*WithAes` API：

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
PlayerData loaded = ObjectSaveRead.ReadWithAes<PlayerData>(
    path,
    aes,
    new HMACVerify(),
    verifyKey);
```

此模式不会执行 PBKDF2。底层不会释放或修改调用方 AES；写入时仍会为每份数据生成随机 IV。HMAC 必须使用单独的 `verifyKey`，不能复用 AES Key。

## 2. 对象读写

```csharp
ObjectSaveRead.Save(
    path,
    data,
    "encryption-password",
    new HMACVerify());

PlayerData loaded = ObjectSaveRead.Read<PlayerData>(
    path,
    "encryption-password",
    new HMACVerify());
```

接口：

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
```

## 3. 字节读写

```csharp
ByteSaveRead.Save(path, bytes, "password", new HMACVerify());
byte[] loaded = ByteSaveRead.Read(path, "password", new HMACVerify());
```

`Read` 会将完整明文放入内存，大文件优先使用流式 API。

## 4. 普通文件加解密

```csharp
FileEncrypt.Encrypt(
    sourcePath,
    protectedPath,
    "password",
    new HMACVerify(),
    progress);

FileEncrypt.Decrypt(
    protectedPath,
    outputPath,
    "password",
    new HMACVerify(),
    progress);
```

输入和输出路径不能指向同一个文件。

## 5. 底层流式 API

```csharp
var options = new ProtectedFileOptions
{
    EncryptionPassword = "password",
    Verify = new HMACVerify()
};

ProtectedFile.Write(
    path,
    stream => WriteContent(stream),
    options,
    progress,
    plaintextLength);

Result result = ProtectedFile.Read(
    path,
    stream => ReadContent(stream),
    options,
    progress);
```

```csharp
public sealed class ProtectedFileOptions
{
    public string EncryptionPassword { get; set; }
    public Aes EncryptionAes { get; set; }
    public byte[] VerifyKey { get; set; }
    public IVerify Verify { get; set; }
}
```

传入回调的流由 `ProtectedFile` 管理。包装 `StreamReader` 或 `StreamWriter` 时应使用 `leaveOpen: true`。

## 6. 校验器

- `HashVerify`：SHA-256，无密钥，只能检测意外损坏。
- `HMACVerify`：HMAC-SHA256，使用自动派生的 HMAC Key 检测主动篡改。
- 自定义 `IVerify`：上层启用 AES 时会收到自动派生的后 32 字节 Key；未启用 AES 时 Key 为 `null`。

`EncryptionPassword` 与 `EncryptionAes` 互斥。`HMACVerify` 在密码模式下使用派生 Key，在直接 AES 模式下要求提供独立的 `VerifyKey`。

## 7. AssetBundle

```csharp
AssetBundle bundle = await AssetBundleLoader.LoadAsync(
    protectedBundlePath,
    "password",
    new HMACVerify(),
    crc: 0);
```

`LoadAsync` 必须从 Unity 主线程调用。文件读取、HMAC 验证和 AES 解密在工作线程执行，AssetBundle 创建回到 Unity 主线程执行。

## 8. 文件格式

```text
密码模式：   [Tag][Salt][IV][Data]
直接 AES：   [Tag][IV][Data]
不加密：     [Tag][Data]
```

- 启用 AES 时，`Data` 是密文。
- 启用 HMAC 时，`Tag = HMAC-SHA256(HMAC Key, Salt? || IV? || Data)`。
- Salt 和 IV 不需要保密，但受到 HMAC 保护。
- 读取与写入必须使用相同模式、AES Key/密码、AES 配置和校验器。

## 9. 升级说明

本次密钥派生与校验范围调整后，新格式与 0.1.0 生成的受保护文件不兼容。旧文件需要使用旧版本读取为明文，再通过新版本重新写入。
