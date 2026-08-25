param(
    [switch]$SingleFile = $true
)

$ErrorActionPreference = "Stop"
Write-Host "🚀 开始构建 AniPlayer 独立便携发行包 (Release Publish)..." -ForegroundColor Cyan

$publishDir = ".\Release_Publish"
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Stop-Process -Name "AniPlayer" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "AnniPlayer" -Force -ErrorAction SilentlyContinue

if ($SingleFile) {
    Write-Host "📦 正在打包为 [单文件托管自包含版 (libmpv-2.dll 独立外置)]..." -ForegroundColor Yellow
    dotnet publish .\AnniPlayer\AnniPlayer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -o $publishDir
} else {
    Write-Host "📦 正在打包为 [散装自包含完整版]..." -ForegroundColor Yellow
    dotnet publish .\AnniPlayer\AnniPlayer.csproj -c Release -r win-x64 --self-contained true -o $publishDir
}

# 确保 libmpv-2.dll 作为独立外部动态库存在 (LGPL合规 & 允许用户自由替换)
if (-not (Test-Path "$publishDir\libmpv-2.dll") -and (Test-Path ".\libmpv-2.dll")) {
    Copy-Item -Path ".\libmpv-2.dll" -Destination "$publishDir\libmpv-2.dll" -Force
}

# 复制皮肤、着色器、语言包和必要组件
Write-Host "📂 正在同步皮肤与运行时资产..." -ForegroundColor Yellow
if (Test-Path ".\Demo\skin") {
    Copy-Item -Path ".\Demo\skin" -Destination "$publishDir\skin" -Recurse -Force
}
if (Test-Path ".\Demo\autocrop.lua") {
    Copy-Item -Path ".\Demo\autocrop.lua" -Destination "$publishDir\autocrop.lua" -Force
}

Write-Host "✅ 发行包构建完成！输出路径: $publishDir" -ForegroundColor Green
