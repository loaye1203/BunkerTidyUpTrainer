# 第三方组件声明

本表只涵盖随 Windows x64 发行包中的第三方二进制文件。组件均为未修改的上游文件；本工具源代码没有包含 Bunker Tidy Up 的游戏程序集或资源。

| 组件 | 发行版本 | 发行包文件 | 许可证 / 上游来源 |
| --- | --- | --- | --- |
| BepInEx | 5.4.23.5 | `Payload/BepInEx/core/BepInEx*.dll` | MIT，版权 Bepis；[源码与许可证](https://github.com/BepInEx/BepInEx/tree/v5.4.23.5) |
| Unity Doorstop | 4.5.0 | `Payload/winhttp.dll` | LGPL-2.1；[v4.5.0 源码](https://github.com/NeighTools/UnityDoorstop/tree/v4.5.0)，完整许可证见 `licenses/UnityDoorstop-LGPL-2.1.txt` |
| HarmonyX | 2.9.0 | `Payload/BepInEx/core/0Harmony.dll`、`BepInEx.Harmony.dll`、`HarmonyXInterop.dll` | MIT；[上游源码与许可证](https://github.com/BepInEx/HarmonyX) |
| Harmony compatibility | 2.0 compatibility assembly | `Payload/BepInEx/core/0Harmony20.dll` | MIT，版权 Andreas Pardeike；见 `licenses/Harmony-MIT.txt` |
| MonoMod | 22.1.29.1 | `Payload/BepInEx/core/MonoMod.*.dll` | MIT，版权 0x0ade 等；[上游源码与许可证](https://github.com/MonoMod/MonoMod) |
| Mono.Cecil | 0.10.4 | `Payload/BepInEx/core/Mono.Cecil*.dll` | MIT，版权 Jb Evain；[上游源码与许可证](https://github.com/jbevain/cecil) |
| Microsoft .NET Runtime / WPF | 构建包记录的 10.0.x | 自包含控制器 `BunkerTidyUpTrainer.exe` 内 | MIT 及 Microsoft 第三方通知；完整随发行包放在 `licenses/Microsoft-DotNet-*` |

`BepInEx` 官方 Windows x64 发行包及 BepInEx 5.4.23.5 的上游依赖可从 [BepInEx v5.4.23.5 Release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5) 下载。构建脚本固定此发行版本并校验归档 SHA-256。

自有项目源码、构建/安装器代码及文档按根目录 [MIT License](LICENSE) 发布。第三方组件不转让其商标或版权所有权。
