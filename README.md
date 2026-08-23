# MiPowerCenter — 电池性能管理（Xiaomi 电脑管家功能独立版）

从「小米电脑管家」中独立提取的 **电池与性能** 工具，用了小米原装的原生性能模块
（`SvrCModuleClrWrapper.dll` + `SvrCModule.dll`）

## 功能

- **性能模式切换**：智能 / 均衡 / 野兽 / 静谧 / 节能 等（与小米电脑管家完全一致的链路与硬件策略）
- **养护充电（充电保护）**：达到设定电量后自动停止充电，保护电池寿命
- **电池信息**：当前电量、充电状态、健康度（full/design 容量比）、充电循环次数、电池型号
- **自包含部署**：`SvrCModule`、C++/CLI 桥接层、原生宿主进程（`XiaomiPcHost.exe`、
  `MiHygieneBroker.exe`）全部打包在应用目录内，**卸载小米电脑管家后仍可正常使用**


## 目录结构

```
yuanma\
├── README.md                     本说明文档
├── BUILD.md                      编译教程
├── build.ps1                     一键编译发布脚本
├── MiPowerCenter\                WPF 应用源码
│   ├── MiPowerCenter.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── Controls\BatteryIconControl.cs
│   ├── Models\                   性能模式定义
│   ├── Services\XiaomiModuleAdapter.cs
│   ├── Assets\                   模式图标
│   └── Components\               小米原生组件（运行时/编译时依赖，免安装管家即可构建）
│   └── drivers\EcIo\             内核驱动备份（XiaomiEcIo.sys / .inf / .cat）
├── tools\                        开发调试工具（探针）源码
│   ├── chargingprobe\            充电保护阈值标定探针
│   ├── battprobe\                模块 RPC 调用探针
│   └── dumpapi\                  wrapper 管理 API 视图
├── references\                   逆向参考材料
│   ├── svc_ascii.txt             SvrCModule.dll 字符串（ASCII）
│   ├── svc_wide.txt              SvrCModule.dll 字符串（UTF-16）
│   └── capture.ps1               窗口截图脚本
└── release\                      预编译自包含产物（可直接运行）
```

## 快速使用（免编译）

直接运行 `release\MiPowerCenter\MiPowerCenter.exe`，无需安装 .NET 运行时，
无需安装小米电脑管家。

## 依赖说明

应用通过 C++/CLI 桥接层（`SvrCModuleClrWrapper.dll`）加载小米原生模块
（`SvrCModule.dll`），与小米电脑管家走完全相同的调用链路：

- 原生 RPC：`{"method":"...","params":{...}}` → `Execute(json)`
- 关键方法：
  - `get/set_workLoad_mode` — 性能模式
  - `get/set_charging_protect` — 充电保护开关
  - `get/set_charging_threshold` — 停止充电电量档位
  - `get_charging_state` — 充电状态
  - `get_battery_info` — 容量 / 循环 / 型号
- 档位持久化：`HKLM\SOFTWARE\MI\SvrCModule\PerformanceMode\POWER\ChargingThreshold`
  应用界面选项：`70/90` → `mode5/mode4`

## 内核驱动（EC）

性能模式 / 充电保护读写 EC 依赖小米原装内核驱动 **`XiaomiEcIo.sys`**
小米电脑管家卸载时会删除该驱动，导致应用「能启动但性能模式/充电保护不可用」。

驱动文件已随源码包备份：
`MiPowerCenter\drivers\EcIo\{XiaomiEcIo.sys, XiaomiEcIo.inf, XiaomiEcIo.cat}`

**独立安装（无需安装管家，需管理员）：**

```powershell
Copy-Item "C:\Program Files\MI\yuanma\MiPowerCenter\drivers\EcIo\XiaomiEcIo.sys" "C:\Windows\System32\drivers\XiaomiEcIo.sys" -Force
sc.exe create MiPowerCenterEcIo type= kernel start= auto binPath= "\SystemRoot\System32\drivers\XiaomiEcIo.sys" DisplayName= "Xiaomi EcIo (MiPowerCenter)"
sc.exe start MiPowerCenterEcIo
```

服务名使用 `MiPowerCenterEcIo`（区别于管家自己的 `XiaomiEcIo`），卸载/重装管家
不会再删掉它，驱动与管家彻底解耦。本机恢复驱动后 `get_workLoad_mode` 即恢复正常。

**卸载驱动（如需）：**

```powershell
sc.exe stop MiPowerCenterEcIo
sc.exe delete MiPowerCenterEcIo
Remove-Item "C:\Windows\System32\drivers\XiaomiEcIo.sys" -Force
```

## Timi 运行时（MiDeviceService）自包含与自愈

性能模式（`get/set_workLoad_mode`）走 `SvrCModule` → 系统加速模块 → 系统服务
**`MiDeviceService`** 承载的会话链路（ `MiScenarioRecognition`/`MiAIBrightness`
管道）。**小米电脑管家卸载时会一并删除 Timi 运行时并注销 `MiDeviceService` 服务**，
导致「应用能启动、充电/电池正常，但性能模式失效」。

本应用已把完整 Timi 运行时内置进应用目录：

```
release\MiPowerCenter\Timi\MiDeviceService\      # 完整运行时（含 MiScenarioRecognition/MiAIBrightness）
release\MiPowerCenter\drivers\EcIo\              # XiaomiEcIo 驱动备份
```

应用启动时（`XiaomiModuleAdapter.EnsureTimiServices`）自动自愈，无需人工操作：

1. 若 `MiDeviceService` 服务不存在 → 以应用内置副本 `sc create` 注册（Auto 启动）
2. 若服务存在但指向的 exe 已被删除 → 改指应用内置副本
3. 确保 `Start=2`（自动启动）并 `sc start` 拉起服务

注册服务需管理员权限（应用通常以管理员运行；权限不足时会在日志记录，可手动以管理员执行上方 `sc` 命令）。

## 常见问题

- **托盘图标不出现**：首次点击 × 时即出现；若被杀软拦截，请将 `MiPowerCenter` 加入信任。
- **卸载管家后性能模式失效**：根因是管家卸载删除了 Timi 运行时并注销 `MiDeviceService` 服务，而**不是** EC 驱动缺失（驱动 `XiaomiEcIo` 已注册为独立服务 `MiPowerCenterEcIo`，不受管家卸载影响）。本应用已内置 Timi 运行时并自动重新注册服务，开机即自愈，无需重装管家。
- **日志**：运行日志在 `%TEMP%\MiPowerCenter.log`。
