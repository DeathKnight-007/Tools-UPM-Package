# Tools UPM Packages

这个仓库集中开发多个可独立安装和独立版本化的 Unity Package Manager 包。`Packages/` 保存正式包，`Tools/` 是 Unity 2022.3 开发与集成测试工程。

| Package | Description | Version |
| --- | --- | --- |
| `com.deathknight.serializablereadwrite` | 序列化、文件加密、完整性校验与受保护 AssetBundle 加载 | 0.1.0 |
| `com.deathknight.filearchive` | ZIP 压缩、安全解压与进度报告 | 0.1.0 |

## Git 安装

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.serializablereadwrite
```

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.filearchive
```

## 开发

使用 Unity Hub 打开 `Tools/`。该工程通过相对 `file:` 路径直接引用仓库中的包，修改包源码后 Unity 会立即重新编译。

每个包独立维护 `package.json`、README、CHANGELOG、文档、示例和测试。发布标签使用包名前缀，例如 `filearchive-v0.1.0`。
