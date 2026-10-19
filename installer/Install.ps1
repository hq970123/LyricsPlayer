# Lyrics Player 安装脚本：信任随包附带的自签名证书，然后安装 MSIX。
$ErrorActionPreference = 'Stop'

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # 导入证书需要管理员权限，自动以管理员身份重新运行
    Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    exit
}

$here = Split-Path -Parent $PSCommandPath
try {
    Write-Host '1/2 信任证书…'
    Import-Certificate -FilePath (Join-Path $here 'LyricsPlayer.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    # 1.1 起包名换成了商店身份，旧版（LyricsPlayer.Widget）先卸掉，免得 WidBar 里出现两个
    Get-AppxPackage -Name 'LyricsPlayer.Widget' -ErrorAction SilentlyContinue | Remove-AppxPackage -ErrorAction SilentlyContinue

    Write-Host '2/2 安装 Lyrics Player…'
    $msix = Get-ChildItem $here -Filter *.msix | Select-Object -First 1
    $deps = @(Get-ChildItem (Join-Path $here 'Dependencies') -Include *.appx, *.msix -Recurse -ErrorAction SilentlyContinue | ForEach-Object FullName)
    if ($deps.Count -gt 0) {
        Add-AppxPackage -Path $msix.FullName -DependencyPath $deps -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    } else {
        Add-AppxPackage -Path $msix.FullName -ForceApplicationShutdown -ForceUpdateFromAnyVersion
    }

    Write-Host ''
    Write-Host '安装完成。打开 WidBar，在控件目录里找到 Lyrics Player，拖到任务栏即可。' -ForegroundColor Green
} catch {
    Write-Host ''
    Write-Host "安装失败：$($_.Exception.Message)" -ForegroundColor Red
}
Read-Host '按回车关闭'
