# Serializable Read Write

Unity 文件序列化、完整性校验、AES 加密和受保护 AssetBundle 加载工具。

## 安装

在 Unity Package Manager 中通过 Git URL 安装：

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.serializablereadwrite
```

## 快速开始

```csharp
using SerializableReadWrite;

ObjectSaveRead.Save(path, data, "encryption-password", new HMACVerify(), "verify-key");
MyData loaded = ObjectSaveRead.Read<MyData>(path, "encryption-password", new HMACVerify(), "verify-key");
```

完整说明参见 `Documentation~`，也可以从 Package Manager 导入 Basic Usage 示例。

## 安全边界

客户端中的密码和密钥可能被提取。本包可以防止意外损坏并提高篡改成本，但不能把不可信客户端变成可信环境。
