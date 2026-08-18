# Changelog

## [Unreleased]

- 移除公开文件 API 的独立 HMAC 密码参数；使用 PBKDF2-SHA256 从 AES 密码派生独立的 AES Key 与 HMAC Key。
- HMAC 校验范围扩展为 Salt、IV 和密文。新格式与 0.1.0 生成的受保护文件不兼容。

## [0.1.0] - 2026-07-21

- 建立标准 UPM 包结构。
- 提供对象和字节文件读写、AES 加密、SHA-256/HMAC-SHA256 校验。
- 提供受保护 AssetBundle 的同步与异步加载。
- 使用官方 `com.unity.nuget.newtonsoft-json` 依赖。
