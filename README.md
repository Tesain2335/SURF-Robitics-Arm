# SURF / Piper VR teleoperation

本分支是 Windows + WSL2 + ROS 2 Humble + Unity 的完整集成快照。下载本分支 ZIP 或 clone 本分支，不要只下载 launcher EXE。**SURF 离线对照实验及其数据不包含在本分支。**

包含：Piper URDF/FK/位置IK、六轴反馈对齐、键盘/VR互斥控制、右摇杆双轴腕部控制、夹爪开合、VR视角重定位、模拟温度分级与视野警示、ROS桥接输入保护、SURF一键启动与六轴修复按钮。

## 新电脑先运行（不需要 ROS 或机械臂）

1. 下载本仓库默认分支的**完整 ZIP**，先解压；不要在压缩包内运行，也不要只下载 EXE。
2. 安装 Unity Hub 和 Unity **6000.4.11f1**。
3. 双击根目录 **Install-Preview.cmd**，会编译启动器并生成桌面 **SURF 一键启动**。
4. 双击桌面快捷方式打开 Unity，打开 `Assets/OutdoorsScene.unity`。首次包导入需要网络。

Unity 不在默认路径时：
```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1 -UnityOnly -Unity 'D:\Unity\6000.4.11f1\Editor\Unity.exe'
```

此模式只打开 Unity，不调用 WSL、ROS 或启动实机；温度显示是模拟数据。实机按钮会提示先完成完整安装。升级时解压到新文件夹并重新运行安装，避免快捷方式仍指向旧目录。

温度标签已修正小物体时文字过窄、靠近视野顶部时文字出界以及窄窗口图例溢出。物体不在相机视野内时其框按设计隐藏；请在 Game 视图查看。头显实际双眼观感仍需在对应设备核对。

## 第一次连接实机：完整安装

1. Windows 10/11 x64，安装 Git、Unity Hub 和 **Unity 6000.4.11f1**（含Windows构建模块）。安装 WSL2、Ubuntu **22.04**，首次打开Ubuntu创建自己的普通用户。
2. 在Ubuntu按ROS官方Humble安装流程安装 **ros-humble-desktop**。此仓库安装器以已安装Humble为前提，不修改你的Ubuntu软件源。
3. 实机使用需要安装 usbipd-win（支持 `state` 与 `attach --wsl`），使用 candleLight USB-CAN（VID 1D50 / PID 606F、gs_usb、1Mbps）。WSL内核必须支持CAN/gs_usb。先在Windows/WSL确认设备可识别。
4. VR使用需要SteamVR、VIVE Business Streaming及匹配的头显驱动。不同头显需要自行配置OpenXR运行时/交互配置。
5. 将本分支解压到本机可写目录。在仓库根目录打开PowerShell，运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-windows.ps1
```

安装过程会在Ubuntu中请求sudo，安装ROS构建依赖和固定的pyAgxArm 1.0.0 / python-can 4.6.1，复制仓库内的ROS源码到全新的 `~/surf_ws` 并编译。不会挂载USB、使能机械臂或发送运动。

Unity或SteamVR路径不同：

```powershell
.\scripts\setup-windows.ps1 -Unity 'D:\Unity\6000.4.11f1\Editor\Unity.exe' -SteamVR 'D:\SteamLibrary\steamapps\common\SteamVR\bin\win64\vrstartup.exe'
```

WSL发行版名称用 `wsl -l -v` 查看，通过 `-Distro` 设置。`-Workspace /home/YOUR_USER/surf_ws` 可指定新的Linux工作区。安装器拒绝覆盖已有src目录；已完成ROS安装时可以 `-SkipRosInstall` 只重新生成Windows配置和快捷方式。

6. 安装成功后，桌面会出现 **SURF 一键启动**。先选择“仅打开软件”，等待Unity导入包，打开 `Assets/OutdoorsScene.unity`。ROS地址由启动器自动更新到当前WSL IP；不用沿用作者的IP。
7. 头显配置和离线软件状态正常后，再按现场流程确认急停、周边空间及机械臂姿态。点击“一键启动实机”会单独确认，并检测USB-CAN、动态获取BUSID、挂载到WSL和使能。**Unity Play由操作者手动开启。**

## 文件结构

- `Assets/`, `Packages/`, `ProjectSettings/`：可由Unity Hub直接添加的工程。两项Unity Robotics依赖以当前commit的源码内嵌，避免首次导入时Git下载失败；其他包按manifest/lock版本安装。
- `launcher/SURF-Launcher.exe`：已编译Windows启动器；`Launcher.cs` / `build.ps1` 可重新构建。
- `launcher/backend/`：ROS服务管理、CAN设置和反馈检查。
- `ros/unity_piper_bridge.py`：统一六轴/夹爪桥接。
- `ros/src/`：实际工作区使用的ROS包快照及许可证，安装时无需获取浮动分支。
- `scripts/setup-windows.ps1`, `scripts/install-wsl.sh`：首次安装。
- `docs/`：控制、数据流、组件来源和验证边界。

`launcher/config.json`由安装器在本机生成，包含当前机器路径，不提交Git。若直接双击EXE提示缺少config，请先完成安装。EXE需与backend和整个工程配套使用。

## 控制

- 右手柄A / Tab：键盘与VR模式切换，避免同时输入。
- 右摇杆上下：J5/link5；左右：J6/夹爪整体旋转，回中保持。
- 键盘A/D、W/S、R/F、T/G、Y/H、Q/E：J1–J6；O/P：夹爪开/闭。
- 右手柄Grip：夹爪两档控制；当前开口命令100mm、闭合0mm，闭合effort接口参数3N（非实测夹持力）。
- 右手柄B / F8：VR视角重新对准机械臂。
- 控制源切换、反馈过期、输入异常时保留现有门控。

温度示例基准25/50/85/130°C，40/65/120°C分级，是模拟显示。尚未接入真实传感器、完整hazard map或地图驱动的实机减速/停止策略。

## 故障排查

- 只有夹爪工作：先停止Play，再使用启动器六轴修复按钮；它处理发布配置和重连，不能修复断电、CAN损坏或追踪失效。
- can0不存在：检查USB挂载、WSL内核CAN模块。不要固定使用作者机器上的1-4或2-1。
- 检测到旧ROS进程：在原终端停止冲突服务；启动器只管理自己启动的进程。
- 不同磁盘SteamVR/VIVE：重新运行安装器指定路径，或修改本地config。
- 安装中断留下新工作区：保留其日志，选另一个新的 `-Workspace` 重试或人工核查现有目录，安装器不自动删除数据。

## 验证范围

在原开发环境已完成Unity/IK、桥接保护和启动器相关离线验证。发布版本额外检查启动器编译、安装脚本语法、桥接输入保护、源码与资产清单。完整第二台干净电脑、不同头显/固件上的现场复现尚未验证；本仓库不宣称物理安全认证或任意动作小于1秒。

首次实际运行时必须确认传感器/反馈、ROS话题、机械臂和夹爪规格。默认目标机器人为Piper六轴和100mm夹爪；75%为驱动速度参数，不是测得的实际速度。停止发命令不等同物理急停。

第三方来源与许可证见 `docs/THIRD_PARTY.md`。上游包保留各自许可证，本集成快照不替换上游授权。

## 本次修复范围（2026-09-22）
本次更新安装入口、启动器预览模式和温度标签布局。其余为9月17日集成快照，后续本机RealSense四画面/HUD等功能尚未纳入该分支；不把本次修复表述为全部本机最新功能同步。离线对照实验和实验数据继续排除。
