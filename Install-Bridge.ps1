$ErrorActionPreference = 'Stop'
$taskName = 'DeskBoxDormElectricity'
$target = Join-Path $env:LOCALAPPDATA 'DeskBoxDormElectricity'
$source = Join-Path $PSScriptRoot 'DormElectricityBridge.exe'
$destination = Join-Path $target 'DormElectricityBridge.exe'
$launcher = Join-Path $PSScriptRoot 'StartBridge.vbs'

if (!(Test-Path -LiteralPath $source -PathType Leaf) -or !(Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw '请先解压发行版压缩包，再从解压目录运行此脚本。'
}

$null = New-Item -ItemType Directory -Path $target -Force
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
$sameExecutable = (Test-Path -LiteralPath $destination -PathType Leaf) -and
    ((Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $destination).Hash)
if (!$sameExecutable) {
    $staged = Join-Path $target 'DormElectricityBridge.exe.new'
    Copy-Item -LiteralPath $source -Destination $staged -Force
    if ($task.State -eq 'Running') { Stop-ScheduledTask -TaskName $taskName }
    foreach ($process in (Get-Process DormElectricityBridge -ErrorAction SilentlyContinue)) {
        if ($process.Path -eq $destination) {
            $process.Kill()
            if (!$process.WaitForExit(10000)) { throw '等待原本机服务退出超时。' }
        }
    }
    Move-Item -LiteralPath $staged -Destination $destination -Force
}
Copy-Item -LiteralPath $launcher -Destination $target -Force

$user = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\wscript.exe" `
    -Argument ('//B //NoLogo "{0}\StartBridge.vbs"' -f $target) -WorkingDirectory $target
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$options = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$null = Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $options -Description '登录后启动宿舍用电查询与微信提醒本机服务。' -Force
Start-ScheduledTask -TaskName $taskName

for ($attempt = 0; $attempt -lt 15; $attempt++) {
    try {
        $health = Invoke-RestMethod -Uri 'http://127.0.0.1:46382/api/health' -TimeoutSec 2
        if ($health.service -eq '宿舍用电' -and !$health.preview) {
            Write-Output '宿舍用电本机服务已启动。现在可以导入同目录下的格子。'
            return
        }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
throw '已登记登录启动任务。当前无法连接本机服务，请检查计划任务 DeskBoxDormElectricity。'
