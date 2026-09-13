<#
.SYNOPSIS
  完整验收：同一对牌手在 3 种对局上各测一遍，并列出每种的得分。

.DESCRIPTION
  为什么不能只跑中速梦镜像 —— 这是已经踩过的坑：

    实测同一个牌手在不同对局上的表现差别很大
      （中速梦镜像 71 分 ｜ 郭龙镜像 决定性 62% ｜ 交叉对局 52.5 / 72.5 分）
    而原来的验收只测中速梦镜像，于是
      **"只改善了交叉对局、镜像局没变"的改动，在验收里完全看不见。**

  两个交叉方向都要跑（先手方用中速梦 / 先手方用郭龙），因为牌手在两边并不对称：
  实测 中速梦侧 52.5 分、郭龙侧 72.5 分。

  判定标准见 BO10-JUDGEMENT.md：**≥ 60 分才算"明显更强"**，而且平分线不是 50 分。

.EXAMPLE
  # 默认：每种对局 25 个 BO10，四种合计 100 个 BO10 = 1000 局
  ./run-acceptance.ps1

.EXAMPLE
  # 快速冒烟：每种 5 个 BO10
  ./run-acceptance.ps1 -Bo10PerConfig 5
#>
[CmdletBinding()]
param(
    [int]$Bo10PerConfig = 25,

    # 第一牌手（挑战者）与第二牌手（锚点）
    [string]$First = 'lookahead',
    [string]$Second = 'lookahead-v2',

    # 其余基准参数原样透传。默认值是"出货配置"。
    [string[]]$AgentArgs = @(
        '--horizon', '1',
        '--alternate-horizon', '3',
        '--rollouts', '60',
        '--rail-margin', '0',
        '--quiet-stats'
    )
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

# 三种对局、四个方向。镜像局两边同牌，所以只需一个方向。
$configurations = @(
    [pscustomobject]@{ Name = '中速梦镜像'; Deck1 = 'DECK-003'; Deck2 = 'DECK-003' },
    [pscustomobject]@{ Name = '郭龙镜像'; Deck1 = 'DECK-002'; Deck2 = 'DECK-002' },
    [pscustomobject]@{ Name = '交叉：中速梦(先) vs 郭龙'; Deck1 = 'DECK-003'; Deck2 = 'DECK-002' },
    [pscustomobject]@{ Name = '交叉：郭龙(先) vs 中速梦'; Deck1 = 'DECK-002'; Deck2 = 'DECK-003' }
)

$logDirectory = Join-Path $PWD 'outputs'
if (-not (Test-Path $logDirectory)) { New-Item -ItemType Directory -Path $logDirectory | Out-Null }

Write-Host ''
Write-Host '========================================================================'
Write-Host " 完整验收：$First  vs  $Second"
Write-Host " 每种对局 $Bo10PerConfig 个 BO10（= $($Bo10PerConfig * 10) 局），四种合计 $($configurations.Count * $Bo10PerConfig) 个 BO10"
Write-Host " 判定线：60 分（见 BO10-JUDGEMENT.md；注意平分线不是 50）"
Write-Host '========================================================================'

$results = foreach ($configuration in $configurations) {
    Write-Host ''
    Write-Host "---------- $($configuration.Name) ----------"

    $project = Join-Path $PWD 'src/Shadowverse.Console/Shadowverse.Console.csproj'
    $arguments = @(
        'run', '--project', $project, '--',
        '--bo10', "$Bo10PerConfig",
        '--deck1', $configuration.Deck1,
        '--deck2', $configuration.Deck2,
        '--p1', $First,
        '--p2', $Second
    ) + $AgentArgs

    $log = Join-Path $logDirectory ("acceptance-$($configuration.Deck1)-$($configuration.Deck2).log")
    $output = & dotnet @arguments 2>&1
    $output | Set-Content -LiteralPath $log -Encoding utf8

    # 只打印 BO10 粒度以上的行：每局的进度行会让输出长到没法看。
    $output |
        Where-Object { $_ -match 'BO10 已出|第一牌手得分率|等强零假设|单侧检验|结论：' } |
        ForEach-Object { Write-Host ('  ' + $_.Trim()) }

    # 最终比分那一行，形如「<A 名字> x 分 ｜ <B 名字> y 分 ｜ 平手 z 个」。
    # 必须排除「BO10 已出 …」和「折算 …」那种同形不同义的中间行，否则会抓错。
    $finalTally = $output |
        Where-Object { $_ -match '分 ｜' -and $_ -notmatch 'BO10 已出' -and $_ -notmatch '折算' } |
        Select-Object -Last 1

    # 决定性牌局只在进度行里，形如「决定性牌局 41:19（各赢一边 140）」。
    # 「（各赢一边」这几个字能把它和结论行区分开（结论行是「决定性牌局 1:1）。」）。
    $decisiveLine = $output |
        Select-String -Pattern '决定性牌局 (\d+):(\d+)（各赢一边' |
        Select-Object -Last 1

    $scoreA = '?'; $scoreB = '?'; $ties = '?'
    if ($finalTally -match '^\s*(.*?)\s+(\d+)\s+分\s*｜\s*(.*?)\s+(\d+)\s+分\s*｜\s*平手\s+(\d+)\s+个') {
        $scoreA = $Matches[2]
        $scoreB = $Matches[4]
        $ties = $Matches[5]
    }
    else {
        Write-Host '  !! 没能解析出最终比分，请直接看日志。' -ForegroundColor Yellow
    }

    $decisive = if ($decisiveLine) {
        "$($decisiveLine.Matches[0].Groups[1].Value):$($decisiveLine.Matches[0].Groups[2].Value)"
    }
    else {
        '?'
    }

    [pscustomobject]@{
        '对局'       = $configuration.Name
        'A 分'       = $scoreA
        'B 分'       = $scoreB
        '平手'       = $ties
        '决定性 A:B' = $decisive
    }
}

Write-Host ''
Write-Host '========================================================================'
Write-Host " 汇总（A = $First，B = $Second；决定性牌局在下面这张表里是 A:B）"
Write-Host '========================================================================'
$results | Format-Table -AutoSize

$totalA = ($results | ForEach-Object { [int]$_.'A 分' } | Measure-Object -Sum).Sum
$totalB = ($results | ForEach-Object { [int]$_.'B 分' } | Measure-Object -Sum).Sum
$grand = $configurations.Count * $Bo10PerConfig
Write-Host "A 合计 $totalA / $grand    ｜    B 合计 $totalB / $grand"
Write-Host ''
Write-Host '日志已写入 outputs/acceptance-*.log'
Write-Host '提醒：不要只看中速梦镜像那一行。只测一种对局时，"只改善了交叉对局"的改动完全看不见。'
