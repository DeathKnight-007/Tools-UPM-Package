# Changelog

## [Unreleased]

- 新增直接传入 `Aes` 的 `*WithAes` API 和 `ProtectedFileOptions.EncryptionAes`，可在持有随机 Key 时跳过 PBKDF2。
- 直接 AES 模式每次写入独立随机 IV；使用 `HMACVerify` 时通过 `VerifyKey` 提供独立校验 Key。
- 移除公开文件 API 的独立 HMAC 密码参数；使用 PBKDF2-SHA256 从 AES 密码派生独立的 AES Key 与 HMAC Key。
- HMAC 校验范围扩展为 Salt、IV 和密文。新格式与 0.1.0 生成的受保护文件不兼容。

## [0.1.0] - 2026-07-21

- 建立标准 UPM 包结构。
- 提供对象和字节文件读写、AES 加密、SHA-256/HMAC-SHA256 校验。
- 提供受保护 AssetBundle 的同步与异步加载。
- 使用官方 `com.unity.nuget.newtonsoft-json` 依赖。
