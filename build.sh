#!/usr/bin/env bash
# SysPulsar ビルドスクリプト
# この環境(Kimi Work の Git Bash)には ProgramFiles/ProgramData 系の標準環境変数がなく、
# そのままでは NuGet restore が "Value cannot be null. (Parameter 'path1')" で失敗する。
# またソリューションレベルのビルドでは MSBuild ワーカーノード再利用で古い環境の
# プロセスが使われることがあるため -nr:false を付ける。
set -e
cd "$(dirname "$0")"

env PATH="$PATH:/c/Program Files/dotnet" \
  ProgramData='C:\ProgramData' \
  ProgramFiles='C:\Program Files' \
  'ProgramFiles(x86)=C:\Program Files (x86)' \
  CommonProgramFiles='C:\Program Files\Common Files' \
  'CommonProgramFiles(x86)=C:\Program Files (x86)\Common Files' \
  ProgramW6432='C:\Program Files' \
  CommonProgramW6432='C:\Program Files\Common Files' \
  TEMP='C:\Users\yu\AppData\Local\Temp' TMP='C:\Users\yu\AppData\Local\Temp' \
  dotnet build -nr:false "$@"

echo
echo "Dump CLI: src/SysPulsar.Dump/bin/Debug/net10.0-windows/SysPulsar.Dump.exe"
