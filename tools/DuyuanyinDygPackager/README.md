# DUYUANJIN DYG 打包工具

这是 Windows 图形工具，用于创建、编辑、校验并导出 DYG v1 `lights-out` 游戏。

- 唯一目标板：`ESP32-S3-Touch-AMOLED-2.16`
- 当前引擎：`lights-out`
- 棋盘：3×3 至 6×6
- 支持单文件 `.dyg`，或导出带 `manifest.json` 的游戏文件夹。
- 文件夹导出后复制到 SD 卡 `/DUYUANJIN/Games/`。

它不会扫描局域网、执行电脑命令、截图或访问设备上的任何文件；只在本地处理用户选择的 DYG 文件。

## GitHub Actions 打包

推送到仓库后，在 Actions 中运行 **Build Windows DYG Packager**。产物为自包含的 `DuyuanyinDygPackager.exe`，不需要用户安装 .NET。
