# 编译教程（BUILD.md）

本文档说明如何从源码编译 MiPowerCenter，以及如何发布自包含、可独立运行的产物。

## 一、环境要求

| 项 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11 x64 |
| .NET SDK | **8.0**（x64），需包含 Windows Desktop / WPF / WinForms 组件 |
| PowerShell | 5.1 或更高 |

> 安装 .NET SDK 后可用 `dotnet --info` 确认版本。
> 无需安装小米电脑管家——本包已自带所需的全部小米原生组件（`MiPowerCenter\Components`）。

## 二、目录约定

```
MiPowerCenter\Components\   小米原生组件（SvrCModule.dll、SvrCModuleClrWrapper.dll、
                            XiaomiPcHost.exe、MiHygieneBroker.exe 及 ~450 个原生 DLL）
```

`MiPowerCenter.csproj` 会按以下顺序选择组件目录（`XiaomiDir`）：

1. 已安装的小米电脑管家 `C:\Program Files\MI\XiaomiPCManager\5.8.1.121`
2. `C:\Program Files\MI\XiaomiPCManager`
3. **本包自带的 `MiPowerCenter\Components`**（推荐，稳定可复现）

## 三、一键编译

在包根目录（含 `build.ps1`）执行：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

产物输出到 `release\MiPowerCenter\`，是**自包含**发布：

- 无需安装 .NET 运行时
- 已内置全部小米原生组件，卸载小米电脑管家后仍可运行
- 直接运行 `release\MiPowerCenter\MiPowerCenter.exe`

## 四、手动编译（可选）

### 1. 构建/发布应用

```powershell
cd MiPowerCenter
dotnet publish MiPowerCenter.csproj -c Release -p:XiaomiDir="...\MiPowerCenter\Components" -o ..\release\MiPowerCenter
```

- 关键参数：
  - `-c Release`
  - `-p:XiaomiDir=...`：指定小米原生组件目录（指向本包 `Components`）
  - `-o ...`：输出目录
- 项目属性：
  - `<SelfContained>true</SelfContained>`、`<RuntimeIdentifier>win-x64</RuntimeIdentifier>` → 自包含
  - `<PublishSingleFile>` **未启用**：单文件发布无法宿主 C++/CLI 桥接层
- 构建完成后，发布目录内应包含：
  - `MiPowerCenter.exe`（应用）
  - `SvrCModuleClrWrapper.dll`、`SvrCModule.dll`、`XiaomiPcHost.exe`、`MiHygieneBroker.exe`
  - 其余 ~450 个原生 DLL 与 .NET 运行时文件

### 2. 部署

将 `release\MiPowerCenter` 整个文件夹复制到目标机器任意目录即可（如
`C:\Program Files\MI\MiPowerCenter\`）。建议目录不含中文、权限可写。

### 3. 自测

发布产物首次启动时可开启内置自测（会执行一次性能模式往返切换并还原）：

```powershell
$env:MIPC_SELFTEST = "1"
.\MiPowerCenter.exe
```

查看运行日志：`%TEMP%\MiPowerCenter.log`

## 五、工具（探针）编译

用于调试/标定小米原生模块 RPC，源码位于 `tools\`：

```powershell
# chargingprobe / battprobe / dumpapi 三个项目结构一致
cd tools\battprobe
dotnet publish -c Release -o publish -p:XiaomiDir="..\..\MiPowerCenter\Components"
# 复制桥接与宿主文件（发布时需随运行目录提供）
copy ..\..\MiPowerCenter\Components\SvrCModuleClrWrapper.dll publish\
copy ..\..\MiPowerCenter\Components\XiaomiPcHost.exe publish\
copy ..\..\MiPowerCenter\Components\MiHygieneBroker.exe publish\
```

用法示例（battprobe，可传 method 与数值参数）：

```powershell
.\publish\BattProbe.exe get_battery_info
.\publish\BattProbe.exe get_charging_threshold
.\publish\BattProbe.exe set_charging_threshold 4        # 90% 档
```

## 六、档位映射说明（重要）

停止充电电量档位（`set_charging_threshold`）与固件 mode 的映射经过本机实测：

| 档位 | mode | 验证方式 |
| --- | --- | --- |
| 80% | 7 | 实测驻留 80% |
| 85% | 5 | 实测驻留 ~85% |
| 90% | 4 | 实测驻留 90% |
| （关闭保护 / 充满） | 1 | 自然充电至 100% |

> 映射定义在 `MainWindow.xaml.cs` 的 `ThresholdMap`。若在其他机型上行为不符，
> 请用 `tools\chargingprobe` 重新标定后再修改。

## 七、EC 内核驱动（部署关键）

性能模式 / 充电保护读写 EC 依赖小米原装内核驱动 `XiaomiEcIo.sys`（设备
`\\.\PhysicalAddressAccess-D2DEBE83-AA54-4DFC-AA7E-2160938BEB88`）。
小米电脑管家**卸载时会删除该驱动**，导致应用能启动但性能模式/充电保护不可用。
因此发布产物内置了驱动备份：`release\MiPowerCenter\drivers\EcIo\`。

部署到新机器（或管家卸载后）需以管理员执行：

```powershell
Copy-Item ".\drivers\EcIo\XiaomiEcIo.sys" "C:\Windows\System32\drivers\XiaomiEcIo.sys" -Force
sc.exe create MiPowerCenterEcIo type= kernel start= auto binPath= "\SystemRoot\System32\drivers\XiaomiEcIo.sys" DisplayName= "Xiaomi EcIo (MiPowerCenter)"
sc.exe start MiPowerCenterEcIo
```

> 服务名用 `MiPowerCenterEcIo`（而非管家自用的 `XiaomiEcIo`），卸载/重装管家不会删除它。
> 验证：`sc query MiPowerCenterEcIo` 为 RUNNING 后，应用内性能模式即恢复。

## 八、常见编译问题

| 问题 | 解决 |
| --- | --- |
| `MSB4062` / 找不到 WPF 支持 | 安装带 Windows Desktop 工作负载的 .NET 8 SDK |
| 发布目录缺 `SvrCModule*.dll` | `XiaomiDir` 未指向包含这些文件的目录，见第四节 |
| 运行报「未检测到小米服务模块」 | 应用目录内缺少 `SvrCModuleClrWrapper.dll`，重新发布 |
| 托盘不显示 | 首次点 × 后出现；检查杀软拦截 |
