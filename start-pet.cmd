@echo off
rem Q Pet —— 双击启动（免安装，直接跑 exe）
setlocal
cd /d "%~dp0"
if not exist "%~dp0Pet.exe" (
  echo [错误] 找不到 Pet.exe
  echo 请确认下载/解压完整，Pet.exe 应与本文件在同一个文件夹里。
  pause
  exit /b 1
)
start "" "%~dp0Pet.exe"
