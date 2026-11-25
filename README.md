# 深圳大学宿舍用电格子

在 DeskBox 桌面上查看宿舍的**剩余电量、预计可用天数、用电记录和缴费记录**。支持丽湖二期，可通过微信接收每日汇报和低电量提醒。

[下载格子和本机服务](https://github.com/Zagadka-3906/SZU-Dorm-Electricity/releases/latest) · [下载官方 DeskBox](https://github.com/Tianyu199509/DeskBox/releases/latest)

## 安装

已在 Windows x64 和官方 DeskBox 1.6.2 上验证。查询时，电脑需要能连接所选校区的宿舍电费系统。

1. 安装官方 DeskBox，从本仓库的发行页下载并解压 `SZU-Dorm-Electricity-win-x64.zip`。
2. 在解压目录打开 PowerShell，运行以下命令。脚本会启动本机服务，并设置为当前 Windows 用户登录时自动启动。

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-Bridge.ps1
   ```

3. 双击 `SZU-Dorm-Electricity.dbxwidget`，在格子工坊点「放到桌面」。
4. 打开格子右上角「⋯ → 连接或刷新数据」，允许格子连接 `127.0.0.1:46382`。接着打开「⋯ → 宿舍与推送设置」，选好房间并保存。

## 使用

- **选择宿舍**。在设置中选择校区和楼栋。丽湖二期还可逐级选择楼层和房间。
- **查看记录**。在格子顶部切换「用电」和「缴费」。两类记录可分别设置为近 3 天、7 天、1 个月、半年或 1 年。
- **预计可用天数**。用剩余电量除以近 7 天有抄表记录的日均用电量。跨日合计按覆盖天数计入。覆盖不足 3 天，或这段时间的已记录用电量为零时，显示「数据不足」。
- **微信提醒**。在设置中填写 SendKey，开启每日汇报或低电量提醒，保存后可点「发送测试消息」。本机服务运行且能访问相关网络时，即使关闭 DeskBox 也会继续检查和推送。 [Server 酱 Turbo](https://sct.ftqq.com/)

本机服务只监听 `127.0.0.1:46382`。房间、记录范围和推送设置保存在 `%LOCALAPPDATA%\DeskBoxDormElectricity\data`，SendKey 保存在当前 Windows 用户的凭据库中。

## 更新

DeskBox 本体通过官方渠道更新。更新宿舍用电格子时，下载本仓库的新版完整包，重新运行安装命令更新本机服务，再导入新版格子。原有房间和推送设置可继续使用。 [最新格子发行版](https://github.com/Zagadka-3906/SZU-Dorm-Electricity/releases/latest)

## 从源码构建

在仓库根目录使用 PowerShell 7 和 .NET 10 SDK 运行 `./Build.ps1`。`dist` 会生成格子包、Windows x64 完整包和 SHA-256 校验文件。只需打包格子时，运行 `./Build.ps1 -WidgetOnly`。

`widget` 保存 DeskBox 格子声明和脚本，`service` 保存本机服务源码。本项目由 [Zagadka-3906](https://github.com/Zagadka-3906) 维护，按 [GPL-3.0-only](LICENSE) 发布。
