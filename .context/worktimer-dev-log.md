# WorkTimer 开发日志

## 2026-06-04：WinUI 3 设置项目环境搭建

### 问题与解决

#### 1. WPF → WinUI 3 切换

**起因**：设置界面最初用 WPF 编写，但遇到 XAML 语法兼容性问题（.NET 8 不支持 Spacing、ColumnDefinitions 缩写等），决定转 WinUI 3。

#### 2. WinUI 3 模板找不到

**错误**：`dotnet new install Microsoft.WindowsAppSDK.Templates` 失败（包不存在）

**原因**：包名错误。正确的包名是在 2026 年 5 月才发布的：
```
Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
```

**包含模板**：
- `winui` / `winui3` — 空白应用
- `winui-navview` / `winui3-navview` — 带导航菜单
- `winui-mvvm` / `winui3-mvvm` — MVVM 模式

#### 3. WinUI 3 无法编译（XamlCompiler）

**错误**：`dotnet build` 失败，找不到 WinUI 相关的 MSBuild 任务。

**原因**：WinUI 3 的 XAML 编译器（XamlCompiler.exe）和 AppX 打包工具（Microsoft.Build.Packaging.Pri.Tasks.dll）不在 .NET SDK 中，需要 Visual Studio 提供。

**解决**：使用 VS 2022 的 MSBuild 编译：
```powershell
& "D:\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" <project>.csproj
```

也可以用 `build-settings.ps1` 一键脚本。

#### 4. COMException 0x80040154（类未注册）

**错误**：运行时 `REGDB_E_CLASSNOTREG`，WinRT 激活失败。

**原因**：WinUI 3 需要 Windows App Runtime 注册到系统。

**解决步骤**：
1. 下载安装 Windows App SDK Runtime（`WindowsAppRuntimeInstall-x64.exe`）
2. 重启电脑
3. 安装后会出现在 `Get-AppxPackage *WindowsAppRuntime*` 列表中

#### 5. `dotnet run` 仍然失败

**错误**：即使安装了运行时，`dotnet run` 依然报 COMException。

**原因**：WinUI 3 解包应用需要临时包标识才能激活 WinRT。模板自带的 `Microsoft.Windows.SDK.BuildTools.WinApp` 包提供了 `winapp` CLI，可以在运行前自动注册临时包标识。

**解决**：
- 保留 `Package.appxmanifest`（即使不用 MSIX 打包，它提供包标识）
- 保留 `Microsoft.Windows.SDK.BuildTools.WinApp` NuGet 包
- `dotnet run` 会自动触发 winapp CLI 注册

#### 6. VS 解决方案配置警告

**错误**：VS 提示 Settings 项目配置不存在（`Debug|Any CPU` 不存在于项目中）

**原因**：WinUI 3 项目只支持 `x86`、`x64`、`ARM64` 平台，不支持 `Any CPU`。但解决方案文件里还是旧配置。

**修复**：编辑 `.sln` 文件，将 Settings 项目的所有平台映射改为 `x64`：
```
{ProjectGUID}.Debug|Any CPU.ActiveCfg = Debug|x64
{ProjectGUID}.Debug|x64.ActiveCfg = Debug|x64
...
```

也要添加 `Debug|ARM64` 和 `Release|ARM64` 到 `SolutionConfigurationPlatforms`。

#### 7. VS 无法启动调试

**问题**：VS 默认用 "WorkTimer.Settings (Package)" 配置（MSIX 打包模式），但这个配置不能正常工作。

**解决**：在 VS 工具栏的启动配置下拉菜单中，选择 **WorkTimer.Settings (Unpackaged)**。`launchSettings.json` 中只保留 Unpackaged 配置即可。

### 当前环境状态

| 组件 | 路径 | 状态 |
|------|------|------|
| .NET SDK | `dotnet --version` → 9.0.314 | ✅ |
| VS MSBuild | `D:\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe` | ✅ |
| Windows App SDK | NuGet 2.1.3 | ✅ |
| WinUI 模板 | `dotnet new winui-navview` | ✅ |
| 运行时 | `Get-AppxPackage *WindowsAppRuntime*` | ✅（1.6 已装） |

### 以后开发注意事项

1. **编译 Settings 项目**：用 `.\build-settings.ps1` 或 VS 的 Unpackaged 配置
2. **`dotnet run`**：可以直接用，winapp CLI 会自动处理包注册
3. **Overlay 项目**（WPF）：直接用 `dotnet run --project src\WorkTimer.Overlay`
4. **双进程架构**：Overlay（WPF）+ Settings（WinUI 3）共享 Core 项目的数据模型和 SQLite
