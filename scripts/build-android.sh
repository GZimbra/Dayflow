#!/usr/bin/env bash
# Build de Release do Dayflow (Android, API mínima 26) + testes + verificação offline.
# Pré-requisitos: .NET SDK 10, workload maui-android, Android SDK (platform 36 + build-tools), JDK 21.
# Assinatura: defina DAYFLOW_KEYSTORE, DAYFLOW_KEY_ALIAS, DAYFLOW_KEY_PASS (senão usa a chave de debug).
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet test tests/Dayflow.Core.Tests -c Release

ARGS=(-f net10.0-android -c Release)
if [[ -n "${DAYFLOW_KEYSTORE:-}" ]]; then
  ARGS+=(-p:AndroidKeyStore=true -p:AndroidSigningKeyStore="$DAYFLOW_KEYSTORE"
         -p:AndroidSigningKeyAlias="$DAYFLOW_KEY_ALIAS"
         -p:AndroidSigningKeyPass="env:DAYFLOW_KEY_PASS" -p:AndroidSigningStorePass="env:DAYFLOW_KEY_PASS")
fi
dotnet publish src/Dayflow.App "${ARGS[@]}"

APK="$(ls -1 src/Dayflow.App/bin/Release/net10.0-android/publish/*-Signed.apk | head -1)"
scripts/verificar-offline.sh "$APK"
echo "APK: $APK"
