#!/bin/bash
# Release 빌드 → dist/Orvio.pext
# .pext = 확장 폴더를 그대로 담은 zip. extension.yaml 이 맨 위에 있어야 함
# (Playnite 10.63 Toolbox/Extensions.cs PackageExtension · Plugins/ExtensionInstaller.cs 확인)
set -euo pipefail
cd "$(dirname "$0")"
DOTNET="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
OUT="Orvio/bin/Release"

# 이전 빌드 잔재가 .pext 에 섞이지 않게 먼저 지움 (다발 39)
rm -rf "$OUT" Orvio/obj/Release dist
"$DOTNET" build Orvio/Orvio.csproj -c Release -nologo -v q

# Playnite 가 설치를 거절하는 파일 (ExtensionInstaller.VerifyExtensionPackage)
for f in Playnite.dll Playnite.Common.dll Playnite.SDK.dll; do
  if [ -n "$(find "$OUT" -name "$f")" ]; then echo "오류: $f 가 출력에 있음" >&2; exit 1; fi
done
[ -f "$OUT/extension.yaml" ] || { echo "오류: extension.yaml 없음" >&2; exit 1; }
[ -f "$OUT/web/index.html" ] || echo "주의: web/index.html 없음 (화면 파일이 아직 없음)" >&2

# 판 번호가 csproj 와 manifest 에서 같은지
ver="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Orvio/Orvio.csproj)"
grep -q "^Version: $ver\$" Orvio/extension.yaml || echo "주의: extension.yaml 의 Version 이 csproj($ver)와 다름" >&2

mkdir -p dist
# Toolbox 처럼 파일 항목만 담음(-D: 폴더 항목 없음)
(cd "$OUT" && zip -q -r -X -D ../../../dist/Orvio.pext . -x '*.DS_Store')
echo "만듦: $(pwd)/dist/Orvio.pext"
unzip -l dist/Orvio.pext
