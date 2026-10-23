# Edge 透明度

摸鱼必备

一个使用 WinUI 3 编写的 Windows 小工具，可调整整个 Microsoft Edge 窗口的透明度。

## 功能

- 中文独立控制面板，自动列出已打开的 Edge 窗口。
- 5%～100% 不透明度滑块，按 1% 调整。
- 100%、80%、60%、40%、20%、5% 快捷挡位。
- 多个窗口分别调整，可恢复当前窗口或全部窗口。
- Ctrl+Alt+0（主键盘）恢复全部窗口；正常退出时自动恢复。
- 不安装浏览器扩展，不修改 Edge 配置。

## 下载运行

从 [Releases](https://github.com/zhou-air/edge-opacity/releases) 下载便携版，解压后运行 `EdgeOpacity.exe`。请保留整个文件夹。

支持 Windows 10 2004 及以上和 Windows 11，x64。发布版包含运行依赖。

透明度作用于整个窗口，包括标签栏和地址栏，同一窗口中的标签页一起生效。数值越小越透明。快捷键被占用时界面会提示。

强制结束程序或断电不会执行退出恢复，此时关闭受影响的 Edge 窗口并重新打开即可。Edge 与本程序需要以相同权限运行。

## 构建

需要 Windows 与 .NET 10 SDK，首次构建需要联网还原依赖。

```powershell
.\publish.ps1
```

输出位于 `dist/EdgeOpacity-win-x64`。使用 C#、WinUI 3 和 Windows App SDK 2.5.1。

## 检查

```powershell
dotnet run --project checks/OpacityChecks.csproj -c Release
```

本机已通过透明度与恢复逻辑检查，以及临时 Edge 窗口上的界面操作检查。`checks/ui-smoke.ps1` 用于已启动应用与隔离测试窗口的操作检查。
