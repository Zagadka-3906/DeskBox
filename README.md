# DeskBox 宿舍用电扩展版

> **非官方发行版。** 本分支基于 [DeskBox 官方项目](https://github.com/Tianyu199509/DeskBox) 1.5.5，由 [Zagadka-3906](https://github.com/Zagadka-3906) 维护，增加深圳大学宿舍用电格子。此扩展版并非原作者发布或维护。

[下载 x64 安装包](https://github.com/Zagadka-3906/DeskBox/releases/download/v1.5.5-dorm.2/DeskBox_DormElectricity_1.5.5_dorm.2_x64.exe) · [查看发行说明](https://github.com/Zagadka-3906/DeskBox/releases/tag/v1.5.5-dorm.2) · [SHA-256 校验文件](https://github.com/Zagadka-3906/DeskBox/releases/download/v1.5.5-dorm.2/DeskBox_DormElectricity_1.5.5_dorm.2_x64.exe.sha256)

## 宿舍用电格子

- 在格子设置中选择校区和楼栋；丽湖二期从新站点逐级选择楼层与房间，其他校区填写房间号。查询需要能够访问对应的深圳大学宿舍用电系统。
- 显示剩余电量和逐日用电量，用分段控件切换用电记录、缴费记录。
- 两类记录分别可选择近 3 天、7 天、1 个月、半年或 1 年。
- 每日用电量按格子的长宽比例竖排或横排；横排时鼠标滚轮可控制横向滚动。
- 三点菜单提供刷新和设置选项。格子放大后仍显示记录列表。

## 安装与更新

本扩展版目前仅提供 Windows x64 安装包，采用 Full Native AOT 构建并内置私有 Windows App Runtime。安装包尚未签名，请按发行页提供的 SHA-256 校验文件核对。

安装前建议备份 `%LOCALAPPDATA%\DeskBox\data`。应用内的更新入口指向 **DeskBox 官方发行版**；直接安装不含此格子的官方版会覆盖扩展版，并可能改写格子布局。要保留宿舍用电格子，应先将官方更新合并到本分支，再重新构建安装包。

宿舍用电格子会用所选校区、楼栋和房间号查询学校内网服务。设置与布局保存在本机。

更多原版功能和系统要求见[完整中文说明](README.zh-CN.md)。本项目沿用原仓库的 [GPL-3.0-only 许可](LICENSE)。
