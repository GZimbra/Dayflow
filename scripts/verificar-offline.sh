#!/usr/bin/env bash
# Verificação offline do APK/AAB final do Dayflow.
# Uso: scripts/verificar-offline.sh caminho/para/app.apk
# Falha (exit 1) se encontrar qualquer permissão de rede, backup habilitado ou SDK de telemetria.
set -euo pipefail
APK="${1:?informe o caminho do .apk}"

AAPT2="${AAPT2:-$(command -v aapt2 || true)}"
if [[ -z "$AAPT2" ]]; then
  # build-tools do Android SDK ou o aapt2 embutido no workload .NET Android
  AAPT2="$(ls -1 "${ANDROID_HOME:-$HOME/Android/Sdk}"/build-tools/*/aapt2 2>/dev/null | tail -1 || true)"
  [[ -z "$AAPT2" ]] && AAPT2="$(ls -1 "$HOME"/.dotnet/packs/Microsoft.Android.Sdk.*/*/tools/*/aapt2 2>/dev/null | tail -1 || true)"
fi
[[ -x "$AAPT2" ]] || { echo "aapt2 não encontrado (defina AAPT2=...)"; exit 2; }

falhou=0
echo "== Permissões declaradas ($APK)"
PERMS="$("$AAPT2" dump permissions "$APK")"
echo "$PERMS"

PROIBIDAS='android.permission.INTERNET|ACCESS_NETWORK_STATE|ACCESS_WIFI_STATE|CHANGE_NETWORK_STATE|CHANGE_WIFI_STATE|NEARBY_WIFI_DEVICES|c2dm.permission.RECEIVE|AD_ID|SCHEDULE_EXACT_ALARM|USE_EXACT_ALARM'
if grep -Eq "uses-permission: name='[^']*($PROIBIDAS)" <<<"$PERMS"; then
  echo "❌ Permissão proibida encontrada:"; grep -E "$PROIBIDAS" <<<"$PERMS"; falhou=1
else
  echo "✅ Nenhuma permissão de rede/telemetria/alarme exato."
fi

echo "== Backup"
XML="$("$AAPT2" dump xmltree --file AndroidManifest.xml "$APK")"
if grep -q 'allowBackup(0x01010280)=false' <<<"$XML"; then echo "✅ allowBackup=false"; else echo "❌ allowBackup não está false"; falhou=1; fi
if grep -q 'dataExtractionRules' <<<"$XML"; then echo "✅ dataExtractionRules presente (API 31+)"; else echo "❌ dataExtractionRules ausente"; falhou=1; fi
if grep -q 'usesCleartextTraffic(0x010104ec)=false' <<<"$XML"; then echo "✅ usesCleartextTraffic=false"; fi

echo "== SDKs de telemetria/rede no pacote"
if unzip -l "$APK" | grep -Eiq 'firebase|crashlytics|appcenter|sentry|play-services|gms|analytics|okhttp'; then
  echo "❌ Artefato suspeito:"; unzip -l "$APK" | grep -Ei 'firebase|crashlytics|appcenter|sentry|play-services|gms|analytics|okhttp'; falhou=1
else
  echo "✅ Nenhum SDK de telemetria/crash/rede encontrado."
fi

exit $falhou
