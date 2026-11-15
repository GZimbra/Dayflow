# Build de Release do Dayflow no Windows (PowerShell 7+).
# Pré-requisitos: .NET SDK 10, `dotnet workload install maui-android`, Android SDK (Visual Studio instala), JDK 21.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

dotnet test tests/Dayflow.Core.Tests -c Release

$args = @("-f", "net10.0-android", "-c", "Release")
if ($env:DAYFLOW_KEYSTORE) {
  $args += "-p:AndroidKeyStore=true", "-p:AndroidSigningKeyStore=$env:DAYFLOW_KEYSTORE",
           "-p:AndroidSigningKeyAlias=$env:DAYFLOW_KEY_ALIAS",
           "-p:AndroidSigningKeyPass=env:DAYFLOW_KEY_PASS", "-p:AndroidSigningStorePass=env:DAYFLOW_KEY_PASS"
}
dotnet publish src/Dayflow.App @args

$apk = Get-ChildItem src/Dayflow.App/bin/Release/net10.0-android/publish/*-Signed.apk | Select-Object -First 1
$aapt2 = Get-ChildItem "$env:LOCALAPPDATA/Android/Sdk/build-tools/*/aapt2.exe" -ErrorAction SilentlyContinue | Select-Object -Last 1
if (-not $aapt2) { $aapt2 = Get-ChildItem "$env:ProgramFiles (x86)/Android/android-sdk/build-tools/*/aapt2.exe" | Select-Object -Last 1 }
$perms = & $aapt2.FullName dump permissions $apk.FullName
$perms
if ($perms -match "INTERNET|ACCESS_NETWORK_STATE|ACCESS_WIFI_STATE|CHANGE_NETWORK_STATE|AD_ID|c2dm") {
  throw "Permissão de rede encontrada no APK."
}
Write-Host "OK: APK sem permissões de rede -> $($apk.FullName)"
