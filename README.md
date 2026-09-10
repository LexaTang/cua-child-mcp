<p align="center"><img src="assets/icon.png" width="180" alt="Cua Child MCP icon"></p>
<h1 align="center">Cua Child MCP</h1>
<p align="center">通用 Windows 子桌面 · 自动启动 · stdio MCP 透明代理</p>

让 MCP 客户端通过独立的 Windows Child Session 操作桌面应用，同时保留主桌面供用户使用。首次连接创建或重连子会话并启动 Cua Driver Worker，后续连接直接复用；工具接口和协议由上游 Cua Driver 提供。

## 使用

在 Windows 上运行便携包的 `cua-child.exe mcp`，或将它添加为 MCP 服务：

```json
{
  "mcpServers": {
    "cua-child": {
      "command": "C:\\Tools\\cua-child-mcp\\cua-child.exe",
      "args": ["mcp"]
    }
  }
}
```

首次启动可能需要管理员启用 Windows Child Sessions：在管理员终端执行一次 `cua-child.exe enable`，然后以普通用户运行 MCP 客户端。便携包自带 .NET 运行时和固定版本 Driver，无需安装 Node、Python 或 Rust。

Codex 可使用以下配置（启动超时给首次创建子桌面留出时间）：

```toml
[mcp_servers.cua-child]
command = 'C:\Tools\cua-child-mcp\cua-child.exe'
args = ["mcp"]
startup_timeout_sec = 120
tool_timeout_sec = 120
```

## 直接控制子桌面

```powershell
./cua-child.exe view
```

显示实时 RDP 控制窗口。点击画面后鼠标键盘进入子桌面；支持窗口缩放、重连、状态显示和托盘隐藏。Ctrl+Alt+Home 释放键盘捕获。关闭窗口仅隐藏，应用与 MCP 继续运行；再次执行 `view` 或双击托盘图标恢复窗口。托盘菜单可退出控制器并断开显示，不注销子桌面。

`cua-child.exe status` 查看状态。`--driver <path>` 可选择其他 Driver 分发目录，`--timeout 90` 控制启动等待。

## 构建

需要 Windows 和 .NET 9 SDK：

```powershell
dotnet build -c Release
./package.ps1 -OutputDirectory ./dist
./dist/cua-child.exe self-test
./integration-test.ps1
```

打包脚本下载 `cua-driver-rs-v0.26.1` 的 Windows x64 发布包，对照官方 SHA-256 清单校验，生成 self-contained 可执行程序和 MCP 配置。Windows CI 也提供构建产物。

图标源图为 `assets/icon.png`，封面直接使用同一图形；`assets/app.ico` 包含 16、20、24、32、40、48、64、128、256 像素版本。执行 `scripts/build-icon.ps1` 可从源图重新生成 ICO。ICO 嵌入 EXE，并用于控制窗口、任务栏与托盘。

## 工作方式与限制

- 当前用户和父会话专属的命名管道；校验服务端 Session ID，避免误连主桌面。
- Mutex 串行化首次启动；独立宿主维持 RDP，MCP stdin EOF 不注销子桌面。
- 使用 Windows RDP ActiveX 的 `ConnectToChildSession`；通过 Task Scheduler `RunEx` 将 Worker 定向启动到子会话。就绪后删除临时任务。
- 标准流按字节转发到上游 `cua-driver mcp --socket`，启动诊断写入 stderr。
- Windows 同时只支持一个已连接的 Child Session。它与主会话共享用户配置；父会话注销也会结束子会话。
- 控制窗口会连接同一个 Child Session，可能取代此前的隐藏 RDP 连接。
- 上游 Cua 的权限限制、逻辑会话空闲回收和工具兼容性仍适用。Windows Child Session 与 MCP 逻辑 session 是不同的生命周期。
- 日志目录：`%LOCALAPPDATA%\CuaChild\<SID>-<parent-session>`。

已在 Windows 实机验证启动/恢复、57 个工具发现、只读工具调用、并发复用和控制窗口连接。全部桌面操作、所有 Windows 版本以及长时间 RDP 断连重连尚未全面验证。

## 来源与许可

本项目是独立的生命周期封装，基于 [Cua Driver](https://github.com/trycua/cua) 的公开 CLI 和 Windows API；不复制整个上游仓库。上游来源基线为 `b4e3caecd709311d613dde29ddac03e29468bb38`，分发依赖版本为 0.26.1。

本项目采用 [MIT License](LICENSE)。Cua Driver 的许可证保留在 [CUA-LICENSE.md](CUA-LICENSE.md)。原创图标的生成方式和提示词见 [assets/PROVENANCE.md](assets/PROVENANCE.md)。本项目不代表 Microsoft、OpenAI 或 Cua 官方。
