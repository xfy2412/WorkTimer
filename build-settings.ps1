$msbuild = "D:\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
$proj = "$PSScriptRoot\src\WorkTimer.Settings\WorkTimer.Settings.csproj"

Write-Host "正在编译 WinUI3 设置项目..." -ForegroundColor Cyan
& $msbuild $proj -nologo -v:q
if ($LASTEXITCODE -ne 0) { Read-Host "编译失败，按回车退出"; return }

$exe = "$PSScriptRoot\src\WorkTimer.Settings\bin\Debug\net9.0-windows10.0.26100.0\win-x64\WorkTimer.Settings.exe"
if (Test-Path $exe) {
    Write-Host "启动设置窗口..." -ForegroundColor Green
    Start-Process $exe
} else {
    Write-Host "未找到 $exe" -ForegroundColor Red
}
