# DeathKnight Net

面向 Unity 项目的网络工具包。

当前版本只建立包、程序集和测试基础设施，尚未承诺具体网络 API。后续功能应按明确场景逐步加入，例如 HTTP、TCP、WebSocket、协议编解码或连接生命周期管理。

## 开发

仓库中的 `Tools` Unity 工程通过本地路径引用本包，修改 `Runtime/` 中的代码后 Unity 会立即重新编译。

## Git 安装

```text
https://github.com/DeathKnight-007/Tools-UPM-Package.git?path=/Packages/com.deathknight.net
```

正式发布前应在地址末尾添加版本标签，例如 `#net-v0.1.0`。
