# ============================================================================================
# Enemy 模块纯逻辑测试的运行脚本。
#
# 位置：<项目根>/Tools/EnemyLogicTests/run.ps1
# 跑法：powershell -NoProfile -ExecutionPolicy Bypass -File Tools/EnemyLogicTests/run.ps1
#
# 为什么需要它：本机 Unity batchmode 启动即退（退出码 0x2231F），
# 所以状态机 / 闸门 / 节流 / 冲刺行程这些纯逻辑只能靠这个脱离 Unity 的工程来验证。
#
# 退出码：0 = 全部通过；非零 = 有失败（审核要求失败必须返回非零）。
# 输出里的 KWC_ENEMY_LOGIC_PASS / KWC_ENEMY_LOGIC_FAIL 供 CI 与人工检索。
#
# 注意：本文件必须带 UTF-8 BOM 保存，否则 Windows PowerShell 5.1 会按 ANSI 读取而乱码。
# ============================================================================================

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here 'EnemyLogicTests.csproj'
$outDir = Join-Path $here 'bin'

Write-Host "构建: $proj"

# 不加 -v quiet：构建失败时错误必须可见，否则只会看到一句「构建失败」而无从下手。
$build = & dotnet build $proj -c Release -o $outDir --nologo 2>&1
if ($LASTEXITCODE -ne 0) {
    # 只回显错误行，避免整段 MSBuild 输出把失败原因淹掉。
    $build | Where-Object { $_ -match 'error' } | ForEach-Object { Write-Host $_ }
    Write-Host ''
    Write-Host 'KWC_ENEMY_LOGIC_BUILD_FAIL'
    exit 2
}

Write-Host ''
& dotnet (Join-Path $outDir 'EnemyLogicTests.dll')
$code = $LASTEXITCODE

exit $code