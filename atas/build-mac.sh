#!/usr/bin/env bash
# ATAS X na macOS: sestaví oba indikátory a zkopíruje je do složky ATAS.
set -e
cd "$(dirname "$0")"
ATASDIR="${1:-/Applications/ATAS X.app/Contents/MonoBundle}"
DEST="$HOME/Library/Application Support/ATAS/Indicators"
command -v dotnet >/dev/null || { echo "Chybí .NET 10 SDK: https://dotnet.microsoft.com/download"; exit 1; }
[ -f "$ATASDIR/ATAS.Indicators.dll" ] || { echo "V '$ATASDIR' není ATAS.Indicators.dll – zadejte cestu jako parametr."; exit 1; }
mkdir -p "$DEST"
for P in DeltaStrengthBar WickAbsorption; do
  echo "=== $P ==="
  dotnet build "$P/$P.csproj" -c Release -p:AtasDir="$ATASDIR" -o "out/$P"
  cp "out/$P/$P.dll" "$DEST/"
done
echo "HOTOVO – restartujte ATAS X."
