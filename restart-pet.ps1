# 重启桌宠（设置面板改完配置后调用；也可手动双击）
# 独立 exe 版：直接拉起 FurinaPet.exe —— 不再需要 PowerShell 启动器、不再每次编译
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here 'FurinaPet.exe'

# 1) 停掉旧实例：优先按 pet.pid，再清掉可能残留的旧（launcher 版）实例
$pidf = Join-Path $here 'pet.pid'
if (Test-Path $pidf) {
  $old = (Get-Content $pidf -Raw).Trim()
  if ($old -match '^\d+$') { Stop-Process -Id ([int]$old) -Force -ErrorAction SilentlyContinue }
}
Get-CimInstance Win32_Process -Filter "Name='FurinaPet.exe'" -ErrorAction SilentlyContinue |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*-File*pet-launcher.ps1*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 700

# 2) 用 WMI 创建：父进程是 WmiPrvSE ⇒ 脱离调用者进程树（关掉 DSH / 关掉这个窗口都不影响它）
if (-not (Test-Path $exe)) { "找不到 $exe"; exit 1 }
$cmdline = '"' + $exe + '"'
$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmdline }
if ($r.ReturnValue -eq 0) { "桌宠已重启（PID $($r.ProcessId)）" } else { "启动失败，ReturnValue=$($r.ReturnValue)" }
