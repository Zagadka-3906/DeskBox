# 深圳大学宿舍用电格子

可导入官方 DeskBox 的自定义格子，搭配 Windows 本机服务查询深圳大学宿舍电量。已在 **DeskBox 1.6.2、Windows x64** 上验证，DeskBox 后续更新可通过官方渠道安装。

**[下载格子和本机服务](https://github.com/Zagadka-3906/DeskBox/releases/latest)** · [下载官方 DeskBox](https://github.com/Tianyu199509/DeskBox/releases/latest)

## 功能

- 显示剩余电量和预计可使用天数，可切换用电记录和缴费记录。预计天数按近 7 天已记录的日均用电计算。查询范围可分别选近 3 天、7 天、1 个月、半年或 1 年。
- 支持丽湖二期按楼栋、楼层、房间查询。查询时电脑需要能访问所选校区的电费系统。
- 可通过 Server 酱向微信发送定时汇报、低电量提醒和测试消息。本机服务随 Windows 用户登录启动，DeskBox 关闭期间也可推送。

## 安装

1. 安装官方 DeskBox 1.6.2 或兼容的后续版本。
2. 从本仓库发行页下载并解压 `SZU-Dorm-Electricity-win-x64.zip`。在解压目录打开 PowerShell，运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-Bridge.ps1`，启动本机服务并设置登录启动。
3. 双击 `SZU-Dorm-Electricity.dbxwidget`，在格子工坊点击「放到桌面」。已安装本机服务时，可单独下载格子文件导入。
4. 打开格子右上角「⋯ → 连接或刷新数据」，允许它访问 `127.0.0.1:46382`。
5. 打开「⋯ → 宿舍与推送设置」，选择房间、记录范围和提醒条件。需要微信提醒时，在本机页面填写 Server 酱 Turbo 的 SendKey，并可发送测试消息。 [Server 酱 Turbo](https://sct.ftqq.com/)

本机服务仅监听 `127.0.0.1:46382`。房间与通知设置保存在 `%LOCALAPPDATA%\DeskBoxDormElectricity\data`，SendKey 保存在当前 Windows 用户的凭据库中。

## 从旧整包迁移

先备份 `%LOCALAPPDATA%\DeskBox\data`，记录旧格子的房间和提醒设置，再安装官方 DeskBox 并按上面步骤导入新格子。原来保存在同一 Windows 用户凭据库里的 SendKey 可以沿用。

## 构建

在仓库根目录使用 PowerShell 7 和 .NET 10 SDK，运行 `./Build.ps1`。`dist` 会生成格子包、Windows x64 压缩包和 SHA-256 校验文件。`./Build.ps1 -WidgetOnly` 只打包格子。

`widget` 保存格子声明和脚本，`service` 保存本机服务及校园查询代码。服务复用了 DeskBox 旧扩展中的查询和凭据存取代码。本仓库按 [GPL-3.0-only](LICENSE) 公开源码。项目由 [Zagadka-3906](https://github.com/Zagadka-3906) 维护，官方程序由 [DeskBox 上游项目](https://github.com/Tianyu199509/DeskBox) 发布。
