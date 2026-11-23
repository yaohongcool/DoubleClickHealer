param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\publish')
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\DoubleClickHealer.App\DoubleClickHealer.App.csproj'
$Output = [System.IO.Path]::GetFullPath($Output)

if (Test-Path $Output) {
    try {
        Remove-Item $Output -Recurse -Force
    }
    catch {
        throw "无法清理输出目录（程序可能正在运行）：$Output。请先退出 DoubleClickHealer 后重试。"
    }
}

dotnet publish $project -c $Configuration -r $Runtime -p:PublishProfile=win-x64 -o $Output --nologo -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码 $LASTEXITCODE"
}

$exe = Join-Path $Output 'DoubleClickHealer.exe'
if (-not (Test-Path $exe)) {
    throw "发布失败：找不到 $exe"
}

$size = (Get-Item $exe).Length
Write-Host ("发布完成: {0} ({1:N1} MB)" -f $exe, ($size / 1MB))

if ($size -gt 70MB) {
    Write-Warning '产物体积超过 70 MB，请确认 win-x64.pubxml 未被修改。'
}
