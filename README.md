# switchTheme

一个简洁的 Windows 定时主题工具，每天按设定时间切换系统和应用的浅色／深色主题。

基于 C#、Windows Forms 和 GDI+ 自绘界面，使用 .NET Framework 4.8，面向 Windows x64。当前版本为 **v0.3.6**。

## 功能

- **每日定时切换**：设置浅色开始和结束时间，支持跨午夜的时间段。
- **手动主题切换**：浅色／深色按钮保留图标和文字；开启定时后，手动选择保持到下一个计划切换时间。
- **开机静默启动**：当前用户登录 Windows 后延迟 10 秒启动，只在托盘运行。
- **托盘与单实例**：关闭窗口继续运行，双击托盘或再次打开程序可唤出设置窗口；托盘菜单支持暂停一小时、恢复和退出。
- **紧凑界面**：320 × 257 DIP，黑白与中性灰配色，使用横线分隔，支持拖动与最小化。
- **本地运行**：无账号、网络请求、定位、遥测和自动更新。

主题切换统一作用于 Windows 系统和应用。其他应用需要选择“跟随系统”，部分应用可能需要重新打开才能响应。

## 下载与使用

在 [Releases](https://github.com/kibuouo/switchTheme/releases) 下载 `switchTheme-win-x64.zip`，解压到一个固定且可写的目录，例如用户文档文件夹。

1. 保持 `switchTheme.exe` 与 `switchTheme.exe.config` 在同一目录，运行 EXE。
2. 在“白天时间”中输入浅色开始和结束时间，例如 `07:00 - 19:00`。使用 24 小时制，两个时间必须不同。
3. 开启“定时切换”；需要登录后后台运行时，开启“开机静默启动”。
4. 点击“保存并应用”。关闭窗口后程序继续在托盘运行；完全退出请使用托盘右键菜单。

首次使用默认关闭定时切换和开机静默启动。浅色时间可以跨午夜，例如 `19:00 - 07:00`。

### 添加到开始菜单

在解压目录打开 PowerShell，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Add-StartMenu.ps1
```

该脚本只创建当前用户的开始菜单快捷方式。之后按 Win 键搜索 **switchTheme** 即可打开。开机静默启动仍由程序中的开关控制。

移动程序后，需要重新创建快捷方式，并在程序中重新保存开机启动设置。

## 开机静默启动

开启并保存后，程序注册 Windows 任务计划程序中的 `switchTheme` 登录任务：

- 仅当前用户登录时触发，延迟 10 秒，使用普通用户权限。
- 直接运行 `switchTheme.exe --background --startup`，不打开控制台或设置窗口。
- 允许电池供电运行，不要求网络，无三天运行时限。
- 重复后台启动不会多开；再次手动打开会激活已有窗口。

关闭“开机静默启动”并保存即可删除登录任务。任务带有隐藏标记，可在任务计划程序中启用“显示隐藏的任务”查看。任务管理器“详细信息”中可查找 `switchTheme.exe`。

升级或更换目录后，可以按已保存的开关更新任务：

```powershell
.\switchTheme.exe --repair-startup
```

## 设置与日志

数据存放在 EXE 同目录的 `data` 文件夹，手动启动与登录启动读取同一份设置：

```text
data/settings.json          每日时间和设置开关
data/settings.previous.json 旧设置迁移备份（如存在）
data/activity.log           运行错误记录
data/startup.log            启动、初始化和退出记录
```

程序所在目录需要有写入权限。移动或升级时保留 `data` 文件夹；发行包不包含任何个人设置或运行日志。

启动日志可用于排查登录启动问题。登录任务的创建和实际后台启动已在开发机器验证；不同机器的真实重启与登录行为仍需分别确认。

## 从源码构建

需要 Windows x64 和 .NET Framework 4.8。脚本使用系统 Framework 编译器，不需要额外的 .NET SDK 或 NuGet 包。

```powershell
# 编译到 dist/switchTheme.Local-stage
.\scripts\build-local.ps1

# 发布到 dist/switchTheme 并生成便携 ZIP
.\scripts\publish.ps1

# 为本地发布版本创建开始菜单入口，按已有设置修复启动任务
.\scripts\install-local.ps1
```

单独创建快捷方式也可以运行：

```powershell
.\scripts\add-start-menu.ps1 -Executable .\dist\switchTheme\switchTheme.exe
```

仓库保留独立检查脚本，可由开发者按需运行：

```powershell
.\scripts\test-local.ps1
```

检查覆盖每日时间边界、跨午夜、夏令时、设置迁移与恢复、后台通知合并、单实例交接状态和下一次唤醒计算。桌面交互、真实注销及重启登录需要另外验证。

### 项目结构

```text
src/switchTheme/              当前 Windows Forms 程序
src/switchTheme/Assets/       程序图标
scripts/                     编译、发布、快捷方式与诊断脚本
tests/switchTheme.Tests/      独立检查程序
CHANGELOG.md                 更新记录
```

调度与设置在 `Settings.cs`，系统主题与事件监听在 `NativeServices.cs`，登录任务在 `StartupTask.cs`，自绘界面在 `SettingsWindow.cs`，托盘与单实例逻辑在 `Program.cs`。

主题监听使用注册表变化通知；定时器按下一次切换边界唤醒。系统时间变化或休眠恢复后重新计算。只有注册表监听失败时才使用每 15 秒的兜底检查。主题通知在后台执行，程序自身界面立即刷新。

## 兼容旧本地版本

内部保留部分 ThemeShift 标识用于兼容旧设置与单实例机制。发布脚本在新数据目录不存在时复制旧 `dist/ThemeShift/data`；设置存储也支持从旧 `%LOCALAPPDATA%/ThemeShift/settings.json` 迁移。旧文件保留。

安装脚本只移除指向同一项目旧发布路径的 ThemeShift 登录任务，并停止该路径下的旧进程。旧 WPF 实现及本机诊断资料不包含在此仓库中。
