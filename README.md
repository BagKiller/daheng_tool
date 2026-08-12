# daheng_tool

VegaBeamTool：光束（Beam）分析上位机，WPF / .NET 8。

**操作手册**：[docs/操作手册.md](docs/操作手册.md)

## 支持的相机

界面 Config 区的 `Camera Model` 下拉框用于选型，两种相机共用全部界面与算法功能：

| 型号 | SDK | 打开方式 |
| --- | --- | --- |
| Daheng Mercury3 | GxIAPINET（`Camera\GxIAPINET.dll`，随仓库提供） | 序列号 |
| Tucsen LiraUV | TUCam（`TUCam.dll`，需另行安装） | 设备索引 |

点 `Scan` 扫描在线设备，从 `Device` 下拉框选中后再 `StartCamera`。切换型号前需先停止相机。

`ExposureTime` 输入框统一使用**微秒（μs）**。TUCam 的曝光属性本身以毫秒为单位，已在 `TucsenCamera` 内部换算，界面上不必区分型号。

## 环境要求

- .NET 8 SDK，编译目标固定为 **x64**（两家 SDK 的原生库均为 64 位）。
- 大恒相机：安装 Galaxy 驱动。
- Tucsen 相机：安装 **TUCam SDK（x64）** 及其 VC++ 2013 运行库（SDK 安装包内的 `redist\vcredist_2013_x64.exe`）。

程序按以下顺序查找 `TUCam.dll`，任一命中即可：

1. 环境变量 `TUCAM_SDK_PATH` 指向的目录
2. 程序目录下的 `TUCam\x64`
3. 程序目录
4. `%ProgramFiles(x86)%\TUCam_SDK\runtime\x64`（SDK 默认安装位置）

## Tucsen 连通性自检

接上相机后运行 `Tools\TucsenProbe`，它会依次检查 DLL 定位、SDK 初始化、设备枚举、曝光/增益/位深的取值范围，以及实际取帧的宽高与像素格式：

```
dotnet run --project Tools\TucsenProbe\TucsenProbe.csproj
```

光束分析链路要求**单色 16bit** 数据流。若自检输出的 `channels` 不为 1，说明相机工作在彩色模式，需要先在算法前增加转灰度处理。
