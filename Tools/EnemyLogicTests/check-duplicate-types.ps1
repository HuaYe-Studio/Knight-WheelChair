# ============================================================================================
# 扫描 Assets 下所有 C# 文件，检查「同一命名空间内重复声明同名类型」。
#
# 为什么需要它：纯逻辑测试（EnemyLogicTests.csproj）只链接了部分源文件
# （例如 PhysicsContactSource.cs 因需要物理替身而没被链接），
# 所以它结构上抓不到「未被链接的文件里多了一份接口定义」这类问题。
# 这个检查面向整个 Assets 目录，与链接范围无关。
#
# 背景：一次实际事故 —— 用脚本改写文件时把接口定义与实现拼重，
# 造成 CS0101 'IContactSource' 重复定义，而所有测试都是绿的。
#
# 退出码：0 = 无重复；1 = 发现重复；2 = 脚本自身出错。
#
# 注意：本文件必须带 UTF-8 BOM 保存，否则 Windows PowerShell 5.1 按 ANSI 读取会乱码。
# ============================================================================================

$ErrorActionPreference = 'Stop'

# 脚本位于 <项目根>/Tools/EnemyLogicTests/check-duplicate-types.ps1，
# $MyInvocation.MyCommand.Path 已经是完整路径，因此上溯三层得到项目根。
$projectRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$root = Join-Path $projectRoot 'Assets'
if (-not (Test-Path $root)) {
    Write-Host "找不到 Assets 目录: $root"
    exit 2
}

# 类型名 -> 声明位置列表。以「命名空间 + 类型名」为键。
$declarations = @{}
$fileCount = 0
$typeCount = 0

$files = Get-ChildItem $root -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue

foreach ($file in $files) {
    $fileCount++
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $relative = $file.FullName.Substring($root.Length).TrimStart('\')

    $currentNamespace = ''
    $braceDepth = 0
    $namespaceDepth = -1

    # #if / #else 分支会让同一个类型合法地出现两次，只统计第一个分支。
    $inElseBranch = $false

    $lineNumber = 0
    foreach ($line in ($text -split "`n")) {
        $lineNumber++

        if ($line -match '^\s*#\s*else') { $inElseBranch = $true; continue }
        if ($line -match '^\s*#\s*endif') { $inElseBranch = $false; continue }

        $nsMatch = [regex]::Match($line, '^\s*namespace\s+([\w\.]+)')
        if ($nsMatch.Success) {
            $currentNamespace = $nsMatch.Groups[1].Value
            continue
        }

        if ($inElseBranch) { continue }

        $typeMatch = [regex]::Match($line, '^\s*(?:public|internal|protected|private)?\s*(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|unsafe\s+)*(class|interface|struct|enum)\s+(\w+)')
        if ($typeMatch.Success) {
            $typeName = $typeMatch.Groups[2].Value

            # 嵌套类型按简单名统计可以接受：这里只关心重复声明。
            $key = "$currentNamespace::$typeName"
            $typeCount++

            if (-not $declarations.ContainsKey($key)) {
                $declarations[$key] = New-Object System.Collections.ArrayList
            }

            [void]$declarations[$key].Add("$relative`:$lineNumber")
        }
    }
}

Write-Host "扫描 $fileCount 个 C# 文件，$typeCount 处类型声明"

$duplicates = $declarations.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 }

if (-not $duplicates) {
    Write-Host 'KWC_DUPLICATE_TYPES_PASS'
    exit 0
}

Write-Host ''
Write-Host '发现重复声明（会导致 CS0101）：'
foreach ($entry in $duplicates) {
    Write-Host "  $($entry.Key)"
    foreach ($location in $entry.Value) {
        Write-Host "      $location"
    }
}

Write-Host ''
Write-Host "KWC_DUPLICATE_TYPES_FAIL: $($duplicates.Count)"
exit 1
