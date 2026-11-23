param(
    [Parameter(Mandatory = $true, HelpMessage = '版本号，例如 1.2.0')]
    [string]$Version,

    [string]$Notes = '',

    [switch]$SkipTests,
    [switch]$SkipGit,
    [switch]$SkipPush,
    [switch]$GitHubRelease
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'src\DoubleClickHealer.App\DoubleClickHealer.App.csproj'
$testProject = Join-Path $root 'tests\DoubleClickHealer.Core.Tests\DoubleClickHealer.Core.Tests.csproj'
$tag = "v$Version"
$versionChanged = $false

function Invoke-Git {
    param([string[]]$Arguments)

    & git -C $root @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') 失败（退出码 $LASTEXITCODE）"
    }
}

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw '版本号格式应为 x.y.z，例如 1.2.0'
}

if ($SkipPush -and $GitHubRelease) {
    throw '不能同时使用 -SkipPush 和 -GitHubRelease'
}

if (-not $SkipGit) {
    $status = & git -C $root status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw '无法读取 git 状态'
    }

    if ($status) {
        throw "工作区有未提交改动，请先提交后再发布：`n$($status -join "`n")"
    }

    $branch = & git -C $root branch --show-current
    if ($branch -ne 'main') {
        throw "请在 main 分支上发布（当前: $branch）"
    }

    if (& git -C $root tag --list $tag) {
        throw "标签 $tag 已存在"
    }
}

if (-not $SkipGit) {
    $content = [System.IO.File]::ReadAllText($project)
    $updated = $content -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"

    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($project, $updated, (New-Object System.Text.UTF8Encoding($false)))
        $versionChanged = $true
        Write-Host "已更新版本号: $Version"
    }
}

if (-not $SkipTests) {
    Write-Host '运行测试...'
    & dotnet test $testProject -c Release --nologo -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw '测试失败，已中止发布'
    }
}

Write-Host '发布单文件...'
& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw '发布失败'
}

$exe = Join-Path $root "artifacts\publish\DoubleClickHealer_$tag.exe"
if (-not (Test-Path $exe)) {
    throw "找不到发布产物: $exe"
}

$zip = Join-Path $root "artifacts\DoubleClickHealer_$tag-win-x64.zip"
$checksum = Join-Path $root "artifacts\DoubleClickHealer_$tag-win-x64.sha256"

if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path $exe -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $checksum,
    "$hash  DoubleClickHealer_$tag.exe`r`n",
    (New-Object System.Text.ASCIIEncoding))

$sizeMb = (Get-Item $exe).Length / 1MB
Write-Host ("产物: {0} ({1:N1} MB)" -f $exe, $sizeMb)
Write-Host "压缩包: $zip"
Write-Host "SHA256: $hash"

if (-not $SkipGit) {
    if ($versionChanged) {
        $messageFile = Join-Path $env:TEMP ("dch-release-" + [Guid]::NewGuid().ToString('N') + ".txt")
        try {
            [System.IO.File]::WriteAllText(
                $messageFile,
                "发布 $tag`n",
                (New-Object System.Text.UTF8Encoding($false)))

            Invoke-Git -Arguments @('add', $project)
            Invoke-Git -Arguments @('commit', '-F', $messageFile)
        }
        finally {
            if (Test-Path $messageFile) {
                Remove-Item $messageFile -Force
            }
        }
    }

    Invoke-Git -Arguments @('tag', $tag)

    if (-not $SkipPush) {
        Invoke-Git -Arguments @('push', 'origin', 'main')
        Invoke-Git -Arguments @('push', 'origin', $tag)
    }
}

if ($GitHubRelease) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw '未安装 GitHub CLI（gh），无法创建 Release。请安装 gh 或手动上传产物。'
    }

    $releaseArgs = @('release', 'create', $tag, $exe, $zip, '--title', $tag)
    if ($Notes) {
        $releaseArgs += @('--notes', $Notes)
    }
    else {
        $releaseArgs += '--generate-notes'
    }

    & gh @releaseArgs
    if ($LASTEXITCODE -ne 0) {
        throw '创建 GitHub Release 失败'
    }
}

Write-Host '发布完成。'
