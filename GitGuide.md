# KWC Git 协作指南

适用本项目七名成员。职责见 [README](README.md)，代码边界见 [Architecture](Architecture.md)。仓库根目录就是包含 Assets、Packages、ProjectSettings 和本文档的目录。

## 1. 五条基本规则

1. `main` 保持可打开、无编译错误、已接入流程可运行。
2. 不在 main 开发、不直接 push main；一个功能一个短分支。
3. 提交到功能分支，发 PR，经 Review 后合并。
4. Owner 主要修改自己的模块，跨模块改动先说明并请主程及相关 Owner Review。
5. 小步提交；每次提交的代码和配套 `.meta`、Prefab/SO 修改一起进入版本。

本次不创建远端、不代填 GitHub 账号、不提前创建教学 Tag。首次建库由主程按下文完成；后续成员使用日常流程。

## 2. 首次建库（主程一次性操作）

先完成 Unity 打开/Play Mode 检查。当前文件夹若还没有 `.git`：

```powershell
git init -b main
git status --short --ignored
git add .
git diff --cached --stat
git diff --cached --check
git commit -m "chore: initialize KWC teaching project"
```

`git add .` 前检查忽略列表，确认 Library/Temp/Logs/UserSettings/Builds 没有进入暂存区。若 Git 提示身份未设置，使用自己的姓名和 GitHub 邮箱设置**本仓库**身份：

```powershell
git config user.name "你的名字"
git config user.email "你的GitHub邮箱或noreply邮箱"
```

在 GitHub 新建**空仓库**，不要重复生成 README/license/gitignore。将下面地址替换为真实地址：

```powershell
git remote add origin https://github.com/你的组织/KnightWheelChair.git
git push -u origin main
```

首次导入是唯一的 main 初始化提交，之后所有变更走 PR。在 GitHub 为 main 设置分支保护/Ruleset：要求 PR、至少一次批准、禁止 force push/删除；公共接口 PR 请主程审查。仓库管理员身份尚未提供，当前未实际设置远端保护。不虚构 CODEOWNERS 账号，收齐七人 GitHub 用户名后再配置。

## 3. 分支命名

使用小写英文和连字符：

|类型|例子|
|---|---|
|功能|`feature/player-movement`、`feature/enemy-fsm`、`feature/combat-weapon`|
|修复|`fix/boss-dash-hit-once`|
|文档|`docs/map-collision-contract`|
|工程维护|`chore/input-package-settings`|

不使用复杂 Git Flow；不要把所有人的工作长期堆在 develop 分支。一个分支只处理一个容易检查的任务。

## 4. 每天开发照着做

### A. 更新并创建分支

先保存 Unity Scene/Prefab，确认当前分支干净。未完成的改动先提交到自己的功能分支，不能直接切走覆盖。

```powershell
git status
git switch main
git pull --ff-only origin main
git switch -c feature/player-movement
```

同名分支已经存在时使用 `git switch feature/player-movement`，不要再 `-c`。`--ff-only` 失败说明本地 main 有分叉，找主程确认，不用 reset/force 强行覆盖。

### B. 开发、检查与提交

在 Unity 用对应版本打开。修改自己模块，Play Mode 检查关联流程；切回 MainMenu 检查入口。Console 有 Compiler Error 不提交为可合并状态。

```powershell
git status --short
git diff
git add Assets/KWC/Modules/Player
git diff --cached --stat
git diff --cached --check
git commit -m "feat(player): add eight-direction movement"
git push -u origin feature/player-movement
```

示例只暂存 Player 目录，按实际改动补上配套 Prefab/Data/父目录 `.meta`。新建目录时，其 `.meta` 位于父目录，不能漏掉。提交前检查是否误改了其他 Owner 资产。之后同一分支再推送只需 `git push`。

### C. 同步 main 并开 PR

长于一天的功能及时同步主线。先保存、提交，再执行：

```powershell
git fetch origin
git merge origin/main
```

处理冲突后再次运行 Unity。到 GitHub 创建 `feature/... → main` 的 Pull Request，可先开 Draft 表示仍在开发。填写模板 → 指定主程/相关 Owner → 根据意见修改 → Review 通过后 **Squash and merge**。合并前确认分支包含最新 main 且验收通过。

### D. 合并后

```powershell
git switch main
git pull --ff-only origin main
git branch -d feature/player-movement
```

Squash 合并后 `-d` 有时会提示“未完全合并”。先确认 GitHub PR 已合并、main 确实有全部改动；可暂时保留本地分支并请主程帮助，不把 `-D` 当成常规命令。远端分支可在 PR 页删除。

## 5. Commit 规范

格式：`类型(可选模块): 说明`。说明改了什么，不写 `update`、`111` 或 `final-final`。

|类型|项目例子|
|---|---|
|feat|`feat(combat): add enemy experience drop`|
|fix|`fix(enemy): prevent repeated dash damage`|
|refactor|`refactor(combat): separate health from weapon`|
|docs|`docs: explain wave timeout cleanup`|
|chore|`chore: enable force text serialization`|

中文说明也可以。不要为凑规范拆成不可编译的中间提交。

## 6. PR 与 Review

仓库附带 `.github/pull_request_template.md`。至少填写：做了什么、如何测试、影响哪些模块、是否改公共接口、是否改 Scene/Prefab/SO、是否改 Packages/ProjectSettings、仍有哪些未确认项。涉及显示/行为可附截图或短录像。

主程重点检查：能否打开和运行；是否破坏已有模块；是否符合 Architecture；是否改公共接口；状态是否重复维护；代码是否适合新生阅读。不要用纯个人风格阻挡正常协作。数值改动要能追溯到设计源，不接受“顺手平衡”或混入 P1。

本项目没有配置声称已经运行的云端 Unity CI。当前验证依赖本机同版本 Unity 和 PR 中的测试记录；需要云端 CI 时另行配置许可证/密钥，禁止将许可证、token、密码提交到仓库。

## 7. Unity 文件该不该提交

|文件 / 目录|处理|
|---|---|
|Assets 内容及 `.meta`|必须一起提交；GUID 用来保持引用|
|Scene / Prefab / SO|Force Text 序列化，一起 Review；文本可比较但不等于可随便合并|
|ProjectSettings|提交；项目级变更由主程 Review，避免随手改层、物理或输入模式|
|Packages/manifest.json、packages-lock.json|提交并锁定一致版本，新增包先说明用途；不各自升级|
|Library / Temp / Obj / Logs / UserSettings|不提交，可由本机再生成|
|Builds / .vs / IDE生成的 csproj、sln|不提交；构建产物和个人IDE缓存由忽略规则排除|
|源美术/音频|仅已批准资源；大型二进制引入前讨论 Git LFS，不临时提交整包素材|

Visible Meta Files 和 Force Text 已纳入项目设置。**不要手动删除 `.meta` 来“修引用”。** 在 Unity Project 窗口移动/改名，连同旧路径删除、新路径与 meta 一起提交。别在文件管理器中只移动文件而丢下 meta；复制资产时应让 Unity 为副本生成新 GUID，不能复制出重复 GUID。

`.gitattributes` 统一文本 LF，将二进制标为 binary；当前不强制安装 LFS，不配置仅自己机器有效的 merge driver。未来使用 UnityYAMLMerge 时也须逐个检查结果，不能当自动正确的保证。

Unity自动生成的YAML空字段会带行末空格，已在属性规则中豁免这种格式告警；原始设计文档和官方TMP资源也保留原有格式。自己的C#与新文档仍正常检查，不要为了消除工具提示去格式化整份官方资源或设计源。

## 8. Scene / Prefab / SO 冲突规避

- `Game.unity` 和 `MainMenu.unity` 是公共集成场景；修改层级/接线前在团队内认领时段，一次一人。主程协调集成。
- Owner 主要在 Prefab Mode 修改 `Prefabs/<自己的模块>`，脚本在 `Modules/<自己的模块>`。别为了测试把自己的场景对象全部保存进 Game。
- Map 维护地图 Prefab；Player 维护 Player 与 Camera；Enemy 维护 Enemy1/Boss；Combat 维护 Projectile/ExpOrb/HealthPack；UI 维护 UI Prefab；Game System 维护 GameManager/SpawnWarning。
- Combat 要改 Enemy Prefab 上的 Health 组件时与 Enemy Owner 协调。组件归属与整份 Prefab 文件的并发编辑权是两件事。
- Data 中同一个 SO 一次一人改；波次数值变更请 Game System 与策划共同确认。公共 Prefab、SO 和 ProjectSettings 的影响写进 PR。
- 编辑前 pull、完成后尽快提交，小 PR 比累计三天的场景重构容易合并。

## 9. 遇到冲突

1. `git status` 看冲突文件，先联系同文件另一位修改者。
2. 普通代码理解双方意图后修改，移除 `<<<<<<<`、`=======`、`>>>>>>>` 标记；不能盲点 Accept Mine/Theirs。
3. Scene/Prefab/SO YAML 或 meta/GUID 冲突看不懂就找主程。保留双方内容的可恢复记录，再确定由谁在 Unity 重做相应改动。
4. 修复后在 Unity 等待编译，检查引用、场景及行为。
5. `git add <已解决文件>` → `git commit` → `git push`。

想撤销**本次尚未完成的 merge**可在确认工作已保存后执行 `git merge --abort`。不要用 `git reset --hard` 或 `git clean -fd` 试错，这会删除未提交工作。协作分支不使用 force push。

## 10. 禁止事项

- 绕过 PR 直接 push main（首次导入除外）。
- `git push --force` 到公共分支，覆盖别人工作。
- 随意删除/重建 meta，提交 Library/Temp/日志/构建/许可证。
- 未沟通修改公共接口、Game State、Packages 或 ProjectSettings。
- 大规模移动其他 Owner 目录或无关格式化，导致无意义冲突。
- 擅自改 GDD Scope、数值或把 P1 混入 P0 PR。

## 11. 教学 Tag

由主程从稳定、可运行的 main 提交创建注释 Tag：

|Tag|阶段目标（按最终课程验收确认）|
|---|---|
|lesson-00-start|工程与公共骨架|
|lesson-01-unity-basic|Unity基础与工程介绍|
|lesson-02-map|地图与碰撞|
|lesson-03-player|输入、移动与相机|
|lesson-04-enemy|FSM与AI|
|lesson-05-ui|图形界面与反馈|
|lesson-06-combat|战斗、经验与通信|
|lesson-07-final|系统管理、发布与完整Demo|

阶段名是规则，不代表当前内容已完成。本次不创建这些 Tag。验收后示例：

```powershell
git switch main
git pull --ff-only origin main
git tag -a lesson-00-start -m "Validated project scaffold"
git push origin lesson-00-start
```

发布后不移动旧 Tag；修正版使用新 Tag（例如 `lesson-03-player-v2`）。查看历史课程建议单独 clone 或在干净目录 `git switch --detach lesson-03-player`，结束后 `git switch main`；不要把脱离分支状态下的练习误当作正常功能分支开发。
