# UI · 曾识宇

本目录放 HUD、主菜单、Pause、结算与 UI 反馈代码；Prefab 交付到 `Assets/KWC/Prefabs/UI`，图标放在 `Assets/KWC/Art/UI/Sprites`。

命名空间 `KWC.UI`，不使用 `.asmdef`。界面只**读取**别人维护的状态、只**发出**操作请求：不扣血、不加经验、不扣成长点、不刷怪、不判胜负、不写 `Time.timeScale`（Architecture 第 124 行）。

TextMeshPro、UGUI、Input System 已是基础依赖；正式 Canvas / EventSystem 使用 `InputSystemUIInputModule`。`DevelopmentLauncher` 是 Game System 的临时英文工程检查面板，正式 UI 接入后由本模块替换。

> 本文件只维护**对外接口**、**界面清单**、**接线状态**与**待确认项**。布局细节与内部实现见代码与注释，不在正文重复。

---

## 零、交付清单与当前状态

### 0.1 已交付文件

| 路径 | 类型 | 内容 |
|---|---|---|
| `UIScripts/MainMenu/GameTitleText.cs` | MonoBehaviour | 标题文本。`Awake` 把字段 `gameTitleText`（默认 `Knight-WheelChair`）写进 TMP 文本 |
| `UIScripts/MainMenu/StartGameButton.cs` | MonoBehaviour | 类名 `StartGameButton`。`Awake` 查找 GameManager，`Start` 给自身 Button 加监听 → 调 `gameManager.StartGame()` |
| `UIScripts/MainMenu/ExitGameButton.cs` | MonoBehaviour | `public SendExitGameRequest()`，当前仅 `Debug.Log`；由 Prefab 上 Button.onClick 持久化绑定 |
| `UIScripts/InGame/HUD.cs` | MonoBehaviour | 8 个 `[SerializeField] private TMP_Text` + `Health` / `PlayerStats` / `SpawnScheduler` 三个数据引用；`OnEnable` 订阅并读初始快照，`Update` 兜底重试；对外暴露 `Refresh()` |
| `UIScripts/InGame/PausePanel.cs` | MonoBehaviour | 3 个按钮（继续/引导/返回主菜单），`Start` 查找 GameManager，`OnEnable`/`OnDisable` 成对挂/摘监听 |
| `Prefabs/UI/MainMenuCanvas.prefab` | Prefab | `MainMenuCanvas` → `GameTitleText` / `ExitGameButton` / `StartGameButton` |
| `Prefabs/UI/HUDCanvas.prefab` | Prefab | `HUDCanvas` → `GameInfo`(波次号、波次时间) + `PlayerInfo`(6 行状态)，**8 个文本引用已全部连好** |
| `Prefabs/UI/PauseCanvas.prefab` | Prefab | `PauseCanvas` → `GameTitleText`、`BackgroundCover`、`ResumeButton`、`GuideButton`、`GameTitleText (1)`、`BackToMenuButton`，三个按钮引用已连好 |
| `Prefabs/UI/SettlementCanvas.prefab` | Prefab | 仅 `Canvas + CanvasScaler + GraphicRaycaster`，**无子节点、无脚本** —— 空壳占位 |
| `Art/UI/Sprites/*.png` | 6 张图标 | HpIcon / EXPIcon / LevelIcon / ATKIcon / ATKSpeedIcon / MoveSpeedIcon，均 2048×2048 |

### 0.2 场景接线状态

| 场景 | 内容 | EventSystem 输入模块 | 结论 |
|---|---|---|---|
| `MainMenu.unity` | GameManager、`MainMenuCanvas`、Preview Camera、Directional Light、Development Launcher（**禁用**，`beginSessionOnStart = 0`） | `InputSystemUIInputModule` | 主菜单可点，入口由 Canvas 提供 |
| `Game.unity` | GameManager、Preview Camera、Directional Light、Development Launcher（**启用**，`beginSessionOnStart = 1`） | `InputSystemUIInputModule` | **一个 UI Canvas 都没有**：`HUDCanvas` / `PauseCanvas` 未实例化，`SettlementCanvas` 实例已移除 |

> **`Game.unity` 目前没有任何玩法对象**：组件只有 Camera / Light / EventSystem / GameManager / DevelopmentLauncher，没有 `Health`、没有 `PlayerStats`、没有 `SpawnScheduler`、没有玩家。因此 HUD 的三个数据引用在该场景里**暂时无处可指**，HUD 只能等 Game System 完成场景接线，或在 `TestSpawnWarningScene`（有 `SpawmSchduler` 与 `TestPlayer`，但仍无 Health/PlayerStats）里部分验证。

### 0.3 已修复（累积记录）

**第一轮**

| # | 原问题 | 修法 |
|---|---|---|
| 1 | `GameStartButton.cs` 内类名为 `StartGameButton`，文件名与类名不一致，MonoBehaviour 会丢失 | 文件重命名为 `StartGameButton.cs`，`.meta` / GUID 未变（`14eb4c35…`），`MainMenuCanvas` 引用继续有效 |
| 2 | `HUD.InitializeUI()` 定义了却从未被调用，不订阅事件 | 新增初始化入口调用它 |
| 3 | `HUD.Update()` 无条件访问 `spawnScheduler`，进场景即每帧 NRE | 加 `if (spawnScheduler == null) return;` |
| 4 | HUD 的 8 个文本引用只连了 2 个，其余为 `{fileID: 0}` | Prefab 上 8 个文本引用全部连好 |
| 5 | 层级里缺 `PlayerMoveSpeedText` 节点，移速无处显示 | 新建/改名 `PlayerMoveSpeedText`（下挂 `MoveSpeedIcon`），并连到 `playerMoveSpeedText` |
| 6 | `Game.unity` 的 EventSystem 仍是旧 `StandaloneInputModule`，新输入下点击会抛异常 | 换成 `InputSystemUIInputModule`（guid `01614664…`，与 MainMenu 一致） |
| 7 | HUD 的 8 个控件是 `public` 字段 | 全部改为 `[SerializeField] private` |

**第二轮**

| # | 原问题 | 修法 |
|---|---|---|
| 8 | `Start` 只订阅事件，不写初始文本，开局显示 Prefab 里的占位文字 | 新增快照读取，订阅后立即刷新 |
| 9 | 快照读取可能撞上 `PlayerStats.EnsureInitialized()` 抛异常 | 读前加 `IsInitialized` 守卫 |
| 10 | `OnHealthChanged` 忽略回调参数，回调里又读一次 `CurrentHp` | 改用回调参数 `current` / `max`，并显示最大 HP |
| 11 | `OnLevelUp` 忽略 `finalLevel` | 改用 `finalLevel` |
| 12 | `Update` 每帧无条件重写波次文本 | 波次号加变化检测；未开波显示 `--`；倒计时 `F1` |

**第三轮（HUD 重写）**

| # | 原问题 | 修法 |
|---|---|---|
| 13 | 首次快照存在时机竞争：`Health`/`PlayerStats` 的 `Initialize` **不发事件**，若初始化晚于 HUD 启用，快照会被静默跳过且永不重试，界面一直停在占位文本 | 快照逻辑抽成返回 `bool` 的 `TakeSnapshot()`；`OnEnable` 立即尝试一次，`Update` 里在 `!snapshotTaken` 时持续重试；并新增 `public void Refresh()` 供流程在初始化完成后主动调用 |
| 14 | 波次号在波间会"提前跳"：`SpawnScheduler.EndWave()` 先 `waveIndex++` 再在 `ClearSpawnState()` 里置 `isInWave = false`，轮询会把**下一波编号**当成当前波显示 | `RenderWave` 用 `spawnScheduler.IsInWave()` 判断，未开波显示 `WaveIndex: --` |
| 15 | Boss 波的 `GetWaveTime()` 返回 `Mathf.Infinity`，`< 0` 判断挡不住，会显示 `WaveTime: Infinity` | 抽出 `FormatWaveTime` / `WaveTimeKey`，`-1`→`--`、`Infinity`→`Boss`、普通波→`F1` |
| 16 | `OnDied` 是空方法且未订阅 `Health.Died`，属死代码 | 订阅 `Died`，只做显示收尾（血量归零展示）；胜负裁决仍归 Game System |
| 17 | 倒计时每帧做字符串插值 + 写 TMP（每帧 GC + TMP 网格重建） | 用 `WaveTimeKey`（0.1 秒一档）做变化检测，只有档位变化才写 |
| 18 | 订阅未先移除，仍存在重复订阅隐患；`initialized` 不在 `OnDisable` 复位 | `Subscribe()` 统一"先 `-=` 再 `+=`"（与 `PausePanel` 风格一致）；`OnDisable` 复位 `subscribed` / `snapshotTaken` / 两个变化检测缓存 |
| 19 | HP / ATK / 攻速 / 移速 用 float 直接插值，会出现 `HP: 66.66667/100` | 新增 `FormatValue`，格式 `"0.#"`（最多一位小数、去掉多余的 0）。**属 dev 默认值**，最终精度待策划确认 |
| 20 | 未使用的 `using System.Collections;` / `System.Collections.Generic;` | 删除 |

> 第三轮改动已用真实程序集编译验证：引用 `UnityEngine.CoreModule.dll`、`Unity.TextMeshPro.dll` 与工程 `Library/ScriptAssemblies/Assembly-CSharp.dll`，**0 警告 0 错误**。括号配平与 11 个序列化字段名均已核对，`HUDCanvas.prefab` 的引用无需重连。尚未在 Play Mode 复验。

### 0.4 仍需处理

**P1（影响正确性或可运行性）**

| # | 问题 | 位置 | 后果 | 修法 |
|---|---|---|---|---|
| 1 | Button.onClick 持久化调用记录的类型是 `KWC.ExitGameButton`，实际类是 `KWC.UI.ExitGameButton` | `MainMenuCanvas.prefab` → ExitGameButton | 点击时找不到目标方法，退出按钮无反应 | 在 Prefab 上重新指定 onClick 目标方法 |
| 2 | 暂停只做 `gameObject.SetActive(false)`，没有向 GameManager 请求恢复 | `PausePanel.cs` `OnResumeBtnClick` | `Paused` 状态从未进入，游戏并未暂停 | 阻塞于 Game System 无 `RequestPause/RequestResume`，见 1.3 |

**P2（规范与体验）**

| # | 问题 | 位置 | 说明 |
|---|---|---|---|
| 3 | `GameObject.Find("GameManager")` 全局查找 ×2 | `StartGameButton.cs`、`PausePanel.cs` | 违反 Architecture 第 54 行「不做全局查找服务」；GameManager 是场景内对象，改为 `[SerializeField] private` 由场景赋值即可 |
| 4 | `using Microsoft.VisualBasic;` 属误加引用 | `PausePanel.cs` 第 3 行 | Unity 工程不应引用，删除 |
| 5 | 成长点、属性阶数、Boss 血条没有任何文本控件 | `HUDCanvas.prefab` | `HUD` 也没有对应字段；`StatsChanged` 目前只刷三项属性值。GDD 第 431 行要求 Boss 血条。需先在 Prefab 建节点再加字段 |
| 6 | 控件字段仍是 `public` | `PausePanel.cs` 三个 Button、`GameTitleText.gameTitleText` | HUD 已改私有序列化，这两处未统一；是否统一由主程定，见 2.1 |
| 7 | 图标 2048×2048、`maxTextureSize = 2048`、未打图集 | `Art/UI/Sprites` | 单张 0.5–2.2 MB，界面里只显示几十像素，属性能项，见 2.2 |
| 8 | 三个 Canvas 适配参数仍是 Unity 默认 | 全部 UI Prefab | `ScaleWithScreenSize` / `800×600` / `MatchWidthOrHeight = 0`，不是设计决策，见 2.4 |
| 9 | 波次号与倒计时仍靠 `Update` 轮询，而非 `WaveChanged` / `WaveEnded` | `HUD.RenderWave` | 已有变化检测，功能正确；若改为订阅事件，波末清空逻辑一并处理 |
| 10 | `Refresh()` 需要流程主动调用才最省 | `HUD.cs` | 现在靠 `Update` 重试兜底，能工作，但数据就绪前每帧多一次 `IsInitialized` 判断；流程若能显式调用 `Refresh()` 更干净 |

---

## 一、对外接口

### 1.1 我方需要别人提供的

| 需求 | 由谁提供 | 类型 / 成员 | 现状 |
|---|---|---|---|
| 玩家当前/最大 HP | Combat `Health` | `CurrentHp`、`MaxHp`、`IsInitialized`、`LifeId`、事件 `HealthChanged(int lifeId, float current, float max)`、`Died(int lifeId)` | 代码可用；**Game 场景里尚无 Health 实例** |
| 玩家经验与等级 | Combat `PlayerStats` | `Exp`、`ExpToNextLevel`、`PlayerLevel`、`GrowthPoints`、`IsInitialized`、事件 `ExperienceChanged`、`LevelUp(int finalLevel)` | 同上 |
| 三项属性值与阶数 | Combat `PlayerStats` | `PlayerAttack`、`AttackSpeed`、`PlayerMovement`、`GetAttributeRank(AttributeType)`、事件 `StatsChanged` | 同上（HP 阶数恒为 0） |
| 属性购买 | Combat `PlayerStats` | `TryUpgradeAttribute(AttributeType)` → bool、`SetPurchasingEnabled(bool)`、`CanPurchaseAttributes` | 可用；HP 购买**未实现**，`Hp` 请求返回 false 且不扣点 |
| 波次号 / 剩余时间 | Game System `SpawnScheduler` | `GetWaveIndex()`、`GetWaveTime()`、`IsInWave()`、`IsInBossWave()`、事件 `WaveChanged(int)`、`WaveEnded` | 代码可用；**Game 场景里尚无 SpawnScheduler 实例** |
| 流程状态 | Game System `GameManager` | `CurrentState`、事件 `GameStateChanged`、`StartGame()`、`RestartGame()`、`EnterUpgrade()`、`CompleteSession(GameState)` | 部分可用，**缺暂停/恢复/返回主菜单** |
| 玩家死亡结果 | Combat `Health.Died` → 由流程裁决 | `Died(int lifeId)`、`GameManager.CompleteSession` | 事件已由 HUD 订阅做显示收尾；**谁调 CompleteSession 未定** |
| Boss HP | Boss 实体上的 Combat `Health` | `CurrentHp`/`MaxHp` + `HealthChanged` | Boss 生成接线未完成，`EnterBossWave` 目前直接拒绝 |
| 中文字体资产 | 主程 / 美术 | TMP Font Asset | **未提供**，见 3.3 |
| 界面文案与属性中文名 | 策划 | 数值设计 N01、N05–N08 | 见 2.3 |

> **初始化顺序要求**：`PlayerStats` 的所有读取属性都会先 `EnsureInitialized()`，未 `Initialize` 就读会**直接抛 `InvalidOperationException`**（不是静默返回 0）。HUD 已用 `IsInitialized` 守卫规避，并在 `Update` 里持续重试直到拿到快照——所以顺序颠倒是安全的，只是数据就绪前界面会是空的。

### 1.2 我方提供给别人的

UI 是消费方，对外只提供「可被 Inspector 绑定」的入口和一个刷新方法，没有业务接口。

| 提供 | 谁使用 | 说明 |
|---|---|---|
| `HUD.Refresh()` | **Game System / 流程** | `public void`。流程在完成 `Health.Initialize` / `PlayerStats.Initialize` 之后调用它，可立即点亮界面，省掉 `Update` 的重试轮询。数据未就绪时安全返回，不抛异常 |
| `StartGameButton`（Button 上挂载） | 主菜单 Prefab | 点击 → `GameManager.StartGame()` |
| `ExitGameButton.SendExitGameRequest()` | Prefab 的 Button.onClick 持久化调用 | `public void` 无参，符合 Inspector 绑定要求；**当前持久化调用类型已失效，见 0.4-1** |
| `GameTitleText.gameTitleText` | Inspector | `public string`，`Awake` 应用到 TMP 文本 |
| `HUD` 的 8 个文本与 3 个数据引用 | Inspector（场景实例上 override） | 全部为 `[SerializeField] private`，只能在 Inspector 赋值，外部代码改不了，也不应改 |
| `PausePanel` 的 3 个 `public Button` | Inspector | 尚未改为私有序列化（见 0.4-6） |

### 1.3 与 Game System 的接口（流程与界面显隐）

现状只有 `StartGame` / `RestartGame` / `EnterUpgrade` / `CompleteSession`。**没有暂停、恢复、返回主菜单**，而 UI 不允许自己写 `Time.timeScale`，所以暂停界面目前只能隐藏自己。

建议由 Game System 提供（UI 只提案，签名待 Owner 确认）：

```csharp
// 待 Game System Owner 确认；UI 只发请求，状态与 TimeScale 策略归流程层
bool RequestPause();             // Playing -> Paused
bool RequestResume();            // Paused  -> Playing
bool RequestReturnToMainMenu();  // 任意状态 -> MainMenu（LoadScene Single）
```

界面显隐应统一由 `GameState` 驱动（当前**没有**这个统一组件，各面板互不感知，见 2.5）：

| GameState | 应显示 | 应隐藏 |
|---|---|---|
| `MainMenu` | MainMenuCanvas | HUD、Pause、Settlement |
| `Playing` | HUDCanvas | MainMenu、Pause、Settlement |
| `Upgrade` | HUDCanvas + 属性升级界面（未实现） | Pause、Settlement |
| `Paused` | PauseCanvas（覆盖 HUD，**建议不隐藏 HUD**） | Upgrade、Settlement |
| `Victory` / `GameOver` | SettlementCanvas（对应文案） | Pause、Upgrade |

> HUD 的订阅已在 `OnEnable`、退订在 `OnDisable`，并成对复位状态，所以用 `SetActive` 切换显隐是安全的（重启用会重新订阅并重读快照）。暂停时仍建议别隐藏 HUD，避免玩家看不到自己血量。

`SpawnScheduler.GetWaveTime()` 的返回值语义必须分别处理：

| 情况 | 返回值 | UI 显示 | 当前实现 |
|---|---|---|---|
| 未开波 | `-1` | `--` | 已处理 |
| 普通波进行中 | 剩余秒数（float，`waveTimer` 从 `NormalWaveDuration` 递减） | 倒计时 | 已处理，`F1` |
| Boss 波 | `Mathf.Infinity` | 「Boss」/ `∞` | 已处理，显示 `WaveTime: Boss` |

波次号的语义陷阱：`EndWave()` 的顺序是「`ClearSpawnState()`（置 `isInWave = false`）→ `waveIndex++` → 发 `WaveEnded`」，所以**波间 `GetWaveIndex()` 已经指向下一波**。HUD 因此只在 `IsInWave()` 为 true 时显示编号，否则显示 `--`。`WaveChanged` 只在开普通波成功时发，`WaveEnded` 只在正常波末发（超时或全灭）。

### 1.4 与 Combat 的接口（显示与购买）

| 事件 / 属性 | UI 的用法 | HUD 现状 |
|---|---|---|
| `HealthChanged(lifeId, current, max)` | **用回调参数**刷新；`lifeId` 可用于忽略过期生命周期的迟到回调 | 已用 `current`/`max` |
| `ExperienceChanged` | 刷新 `Exp / ExpToNextLevel`。`ExpToNextLevel` 是「当前级升到下一级的**完整门槛**」，不是「还差多少」 | 已实现 |
| `LevelUp(int finalLevel)` | 连升多级只通知一次，直接刷最终等级 | 已用 `finalLevel` |
| `StatsChanged` | 购买成功**和**每次成功加经验都会发 → 一次刷新三项属性、阶数与成长点 | 刷三项属性；**成长点/阶数无控件**（0.4-5） |
| `Died(int lifeId)` | 死亡表现 | 已订阅，只做血量归零显示；胜负与结算归 Game System |
| `TryUpgradeAttribute(AttributeType)` | 返回值只表示「是否买成功」；UI 不保存自己的成长点副本去自行扣减 | 未实现（升级界面缺失） |
| `GetAttributeRank(AttributeType)` | 显示单项属性阶数。`Hp` 恒返回 0 且购买返回 false → **升级界面不要给 HP 做可点按钮** | 未使用 |

```mermaid
flowchart LR
    subgraph 其他模块（数据所有者）
        C[Combat Health / PlayerStats]
        G[Game System GameManager / SpawnScheduler]
    end
    subgraph UI（只读显示 + 只发请求）
        H[HUD]
        P[PausePanel]
        M[MainMenuCanvas]
        S[SettlementCanvas]
    end
    C -- 只读属性 + 事件 --> H
    G -- 只读属性 + 轮询 --> H
    G -- GameStateChanged --> P
    G -- GameStateChanged --> S
    M -- StartGame 请求 --> G
    G -- 初始化完成后调用 Refresh --> H
    P -- 暂停/恢复/返回请求（未实现） --> G
    S -- 重开请求 --> G
    H -- TryUpgradeAttribute（升级界面用） --> C
```

### 1.5 生命周期与订阅约定

1. **成对订阅**（Architecture 第 59 行：`OnEnable` 绑定 / `OnDisable` 解绑）。
   - `PausePanel`：`OnEnable` 先 Remove 再 Add，`OnDisable` 全部 Remove。
   - `HUD`：`OnEnable` → `Subscribe()`（先 `-=` 再 `+=`）+ `TakeSnapshot()`；`OnDisable` → `Unsubscribe()` + 复位 `subscribed` / `snapshotTaken` / `lastWaveIndex` / `lastWaveTimeKey`。
2. **订阅后先读一次快照**：`HUD` 由 `TakeSnapshot()` 完成；数据未就绪时由 `Update` 重试，流程也可主动调 `Refresh()`。
3. **引用注入**：不做全局查找。`GameObject.Find("GameManager")` 应改为序列化字段由场景赋值（见 0.4-3）。
4. **时间尺度**：若 Game System 最终选择 `Time.timeScale = 0` 暂停，UI 的倒计时、动效必须使用 unscaled 时间。HUD 的波次倒计时用 `SpawnScheduler.GetWaveTime()`（由调度器自己按 `Time.deltaTime` 推进），暂停策略确认后需复查。
5. **不缓存可写状态**：HUD 只缓存 `lastWaveIndex` / `lastWaveTimeKey` 两个**显示用**变化检测值，不参与任何结算逻辑；HP、经验、成长点一律实时读 Combat。
6. **事件里不做重活**：Combat 的事件是同步执行的，监听者只负责显示，不能在回调里再购买、再结算或抛异常中断后续通知。

---

## 二、待确认项

分五类：**阻塞联调**（不定就写不下去）、**需要别人交付**、**需要策划拍板**、**dev 占位值**、**未实现范围**。

### 2.1 阻塞联调：必须主程 + 对应 Owner 确认

| 项 | 现状 | 影响 |
|---|---|---|
| Game 场景的玩家与 SpawnScheduler | `Game.unity` 里没有 Health / PlayerStats / SpawnScheduler | HUD 的三个数据引用无处赋值，无法在正式场景验证 |
| UI 显隐由谁驱动 | 无 `UIRoot`/`UIManager`；`GameStateChanged` 无人订阅 | 各面板互不感知，四个 Prefab 里只有两个进过场景 |
| 暂停 / 恢复流程入口 | `GameManager` 无 Pause/Resume | 暂停界面无法真正暂停，只能隐藏自己 |
| 返回主菜单入口 | 无 | `BackToMenuButton` 点了没反应 |
| 结算界面触发与文案 | 只有空 Canvas 壳；`CompleteSession` 的调用方未定 | Victory / GameOver 无界面 |
| 属性升级界面的时机与关闭 | `EnterUpgrade()` 有实现但没人调用；「何时开下一波」未定 | 升级界面无处发「下一波」请求 |
| 玩家死亡 → 结算的裁决顺序 | 同时死亡优先级未定（GDD 第 509 行） | 结算文案可能显示错 |
| Boss 血条数据源 | Boss 未接 Combat Health；普通敌人用 `FactHealthSource` 占位 | Boss HP 无法显示 |
| 谁负责调用 `HUD.Refresh()` | 当前只能靠 `Update` 重试 | 不是错误，只是不如流程显式调用干净 |
| 控件字段可见性 | HUD 已改私有序列化；`PausePanel` 三个 Button、`GameTitleText.gameTitleText` 仍为 public | 是否统一由主程定 |

### 2.2 等待其他 Owner 交付

| 项 | 依赖 | 现状 |
|---|---|---|
| Game 场景玩法对象接线 | Game System + Combat | 场景里没有玩家、Health、PlayerStats、SpawnScheduler |
| 「玩家数据初始化完成」的通知 | Combat / Game System | 初始化不发事件，UI 靠自己轮询或流程主动调 `Refresh()` |
| Boss HP 与 Boss 波 | Enemy + Combat + Game System | `EnterBossWave` 占位，只拒绝并记日志；`IsInBossWave()` 恒为 false |
| 每波结束打开升级界面的通知 | Game System | `WaveEnded` 已有，但「补满 HP → 开升级」的编排未接 |
| 中文字体资产 | 主程 / 美术 | TMP 默认字体不含中文，当前界面全用英文缩写规避 |
| 图标压缩与图集 | 主程 + 美术 | 6 张 2048×2048 PNG，单张 0.5–2.2 MB，未做 Sprite Atlas |

### 2.3 需要策划拍板（当前取保守读法）

| 项 | 当前实现 | 说明 |
|---|---|---|
| HUD 显示哪些项 | HP（当前/最大）/ EXP / LV / ATK / 攻速 / 移速 + 波次号 + 波次时间 | GDD 第 431 行要求「波次、剩余时间、Boss 血条」；**成长点、属性阶数、Boss 血条均未显示** |
| HP 显示形式 | 纯数字文本 `HP: 100/100` | 无 Slider / Image 血条；GDD 未指定 |
| 数值显示精度 | `FormatValue` 用 `"0.#"`，最多一位小数 | 取整还是保留几位小数需确认（见 0.3 第三轮第 19 条） |
| 倒计时显示精度 | `F1`（0.1 秒），每 0.1 秒刷新一次 | 是否只显示整秒 |
| 属性升级界面形式 | 未实现 | 确认/关闭方式、是否必须花完成长点、何时开下一波（GDD 第 441 行） |
| 暂停入口按键 | 未实现（无 Esc 监听） | GDD 第 443 行待确认 |
| 结算统计内容 | 未实现 | 击杀数 / 到达波次 / 存活时间待定 |
| 界面语言 | 英文（HP/EXP/LV/ATK、Start/Exit） | 是否中文界面待定 |
| 分辨率与适配 | 三个 Canvas 均为 `ScaleWithScreenSize` / `800×600` / `MatchWidthOrHeight = 0` | `800×600` 是 Unity 默认值，不是设计决策 |

### 2.4 dev 占位值（**不是设计值**，确认后必须替换）

| 字段 / 位置 | 默认 | 收谁确认 |
|---|---|---|
| CanvasScaler `Reference Resolution` | 800×600（四个 Canvas 一致） | 主程 / 策划 |
| CanvasScaler `Match Width Or Height` | 0（只匹配宽度） | 主程 |
| HUD 倒计时刷新档位 | 0.1 秒（`WaveTimeKey`） | 主程 / 策划 |
| HUD 数值格式 | `"0.#"`（`FormatValue`） | 策划 |
| HUD 文本前缀 | `HP: `、`EXP: `、`LV: `、`ATK: `、`ATKSpeed: `、`MoveSpeed: `、`WaveIndex: `、`WaveTime: ` | 策划（是最终文案还是临时调试文本） |
| 图标 `maxTextureSize` | 2048（导入默认） | 主程 + 美术 |
| 各按钮 TMP 文案 | 编辑器里手填 | 策划 |
| `GameTitleText.gameTitleText` | `Knight-WheelChair` | 策划（是否需要中文名/副标题） |

### 2.5 未实现 / 未接线（不在本轮范围，不是 bug）

- **属性升级界面**（Upgrade）：无 Prefab、无脚本，P0 缺口。
- **结算界面**：`SettlementCanvas` 只有空 Canvas，无脚本、无文本。
- **Boss 血条**：无任何控件。
- **成长点 / 属性阶数显示**：HUD 未显示，Prefab 里也没有对应文本节点。
- **暂停真正生效**：UI 侧只隐藏自己，无 `TimeScale`/逻辑开关。
- **返回主菜单、退出游戏**：只有 `Debug.Log`。
- **游戏引导**（GuideButton）：空实现。
- **键盘暂停入口**（Esc）：无。
- **统一显隐管理**（UIRoot / UIManager）：无，四个面板互不感知，也没有订阅 `GameStateChanged`。
- **HUD 与 Pause 进场景**：两个 Prefab 都还没实例化到任何场景。
- **UI 动效 / 音效**：P1，未批准。

---

## 三、界面清单与资源

### 3.1 Prefab 与层级

```text
MainMenuCanvas            Canvas + CanvasScaler + GraphicRaycaster
├── GameTitleText         [GameTitleText] → Text (TMP)
├── ExitGameButton        [ExitGameButton] → Text (TMP)   ← onClick 类型不匹配，见 0.4-1
└── StartGameButton       [StartGameButton] → Text (TMP)

HUDCanvas                 Canvas + CanvasScaler + GraphicRaycaster + HUD
├── GameInfo
│   ├── WaveIndexText     波次号      → waveIndexText      ✅
│   └── WaveTimeText      剩余时间    → waveTimeText       ✅
└── PlayerInfo
    ├── PlayerHealthText    ← HPIcon        → playerHealthText      ✅
    ├── PlayerEXPText       ← EXPIcon       → playerExperienceText  ✅
    ├── PlayerLVText        ← LVIcon        → playerLevelText       ✅
    ├── PlayerATKText       ← ATKIcon       → playerAttackText      ✅
    ├── PlayerATKSpeedText  ← ATKSpeedIcon  → playerAttackSpeedText ✅
    └── PlayerMoveSpeedText ← MoveSpeedIcon → playerMoveSpeedText   ✅
       （数据引用 playerHealth / playerStats / spawnScheduler 仍为 0，需场景注入）

PauseCanvas               Canvas + CanvasScaler + GraphicRaycaster + PausePanel
├── GameTitleText
├── BackgroundCover       半透明遮罩
├── ResumeButton          → Text (TMP)     → resumeGameButton   ✅
├── GuideButton           → Text (TMP)     → guideButton        ✅
├── GameTitleText (1)
└── BackToMenuButton      → Text (TMP)     → mainMenuButton     ✅

SettlementCanvas          Canvas + CanvasScaler + GraphicRaycaster（无子节点、无脚本）
```

`HUDCanvas` 的 8 个文本引用已全部连好；`PauseCanvas` 的 3 个按钮引用已全部连好（按钮监听由代码在 `OnEnable` 挂载，Prefab 上没有持久化 onClick）。

### 3.2 图标资源

| 文件 | 用途 | 尺寸 | 文件大小 | 导入设置 |
|---|---|---|---|---|
| `HpIcon.png` | HP 行 | 2048×2048 | 2.16 MB | Sprite / Single / max 2048 / Normal 压缩 |
| `EXPIcon.png` | EXP 行 | 2048×2048 | 1.69 MB | 同上 |
| `LevelIcon.png` | 等级行 | 2048×2048 | 0.49 MB | 同上 |
| `ATKIcon.png` | 攻击力行 | 2048×2048 | 1.00 MB | 同上 |
| `ATKSpeedIcon.png` | 攻速行 | 2048×2048 | 1.08 MB | 同上 |
| `MoveSpeedIcon.png` | 移速行 | 2048×2048 | 1.16 MB | 同上 |

图标在界面上只有几十像素，2048 明显过大；建议 `maxTextureSize = 256` 并考虑打 Sprite Atlas（性能项，待主程确认，见 2.2）。

### 3.3 字体与中文

- 工程已导入 TMP Essential Resources；界面当前全部是英文缩写与数字，所以默认字体可用。
- TMP 默认字体（LiberationSans）**不覆盖中文**。要出中文界面必须：选定可分发的中文字体 → 生成 Font Asset（推荐静态子集）→ 把 `TMP Settings` 的 Default Font 指向它。授权与来源按 `Art/README.md` 记录，不下载未授权素材。
- 未确认界面语言前不要批量把文案改成中文，否则会立刻出现方块字。

---

## 四、最小接线示例

### 4.1 场景接线步骤（由 Game System / 主程执行，UI 不自己找对象）

```text
前置：Game 场景先要有玩家实体（挂 Health + PlayerStats）与 SpawnScheduler（见 2.1）

1) 打开 Game.unity（EventSystem 已是 InputSystemUIInputModule，无需再改）
2) 拖入 Prefabs/UI/HUDCanvas.prefab 与 Prefabs/UI/PauseCanvas.prefab
3) 在**场景里的 HUDCanvas 实例**上（不是 Prefab Mode），覆盖赋值：
     playerHealth    ← 玩家实体上的 Health 组件
     playerStats     ← 玩家实体上的 PlayerStats 组件
     spawnScheduler  ← 场景里的 SpawnScheduler
   （8 个文本引用已在 Prefab 内连好，不需要再动）
4) 玩家实体 Initialize 完成后，调用一次 hud.Refresh() 最干净
   （即使不调用，HUD 也会在 Update 里自动补快照，只是晚一帧并多几次判断）
5) PauseCanvas 默认设为未激活，由 GameState 切换
```

> 注意：`HUD` 的所有字段都是 `[SerializeField] private`，只能像上面这样在 Inspector 里赋值；赋值点必须在**场景实例**上，因为 Prefab Mode 里拖不到场景对象。

### 4.2 HUD 的刷新机制（接线时要知道的三件事）

```csharp
// 1) 初始化不发事件：订阅后必须主动读一次快照，否则界面停在 Prefab 里的占位文本。
void OnEnable()
{
    Subscribe();        // 先 -= 再 +=，避免重复订阅
    TakeSnapshot();     // 数据就绪就立即点亮；未就绪返回 false
}

// 2) 数据可能晚于 HUD 启用才初始化，而 Health / PlayerStats 的 Initialize 不发事件，
//    所以 Update 里持续重试，直到拿到第一份快照。流程也可主动调 Refresh() 跳过这段轮询。
void Update()
{
    if (!snapshotTaken && TakeSnapshot()) return;
    RenderWave(false);
}

// 3) 波次号只在真正处于波次中时显示：EndWave() 会先推进 waveIndex 再置 isInWave = false，
//    波间的 GetWaveIndex() 已经指向下一波，直接显示会让玩家误以为已经进了下一波。
int waveIndex = spawnScheduler.IsInWave() ? spawnScheduler.GetWaveIndex() : -1;
```

显示的三种波次时间语义（不要合并成一个分支）：

| `GetWaveTime()` | 显示 |
|---|---|
| `-1` | `WaveTime: --` |
| `Mathf.Infinity` | `WaveTime: Boss` |
| 剩余秒数 | `WaveTime: 12.3`（`F1`） |

### 4.3 暂停面板建议改法（修 0.4-2 / 0.4-3 / 0.4-4）

```csharp
[SerializeField] private GameManager gameManager;   // 场景赋值，不用 GameObject.Find

void OnResumeBtnClick()
{
    // RequestResume 待 Game System 提供（见 1.3）；在它落地前这个按钮只能"隐藏面板"
    if (gameManager != null && gameManager.RequestResume())
    {
        gameObject.SetActive(false);
    }
}
```

同时删除 `using Microsoft.VisualBasic;`。

### 4.4 退出按钮修法（0.4-1）

在 `MainMenuCanvas.prefab` 上选中 `ExitGameButton` → `Button.onClick` 列表 → 把失效项删掉，重新拖入该对象并选择 `ExitGameButton.SendExitGameRequest`。保存后检查 `MainMenuCanvas.prefab` 里 `m_TargetAssemblyTypeName` 应为 `KWC.UI.ExitGameButton, Assembly-CSharp`。

### 4.5 升级界面购买按钮的建议写法（尚未实现，作为接口约定）

```csharp
// UI 只发请求；成败、扣点、重算全部归 Combat。
// 不要给 HP 做可点按钮：TryUpgradeAttribute(AttributeType.Hp) 返回 false 且不扣点。
void OnBuyAttackClick()
{
    bool ok = playerStats.TryUpgradeAttribute(AttributeType.Attack);   // 成功会自动发 StatsChanged
    buyResultText.text = ok ? "购买成功" : "点数不足或当前不可购买";
}
```

---

## 五、Reviewer 最小复现步骤

| 检查 | 步骤 | 预期 |
|---|---|---|
| 主菜单 | 打开 `MainMenu.unity` → Play | 标题显示 `Knight-WheelChair`；点 **StartGameButton** 载入 Game 场景 |
| 退出按钮 | 在主菜单点 ExitGameButton | Console 出现「已发送“退出游戏”请求」。**若没有输出，即 0.4-1 未修** |
| Missing Script | 两个场景 Console | 应**无** Missing Script / 找不到类型警告 |
| 开发入口 | 打开 `Game.unity` → Play | `DevelopmentLauncher` 自动 `BeginSession()`，状态变为 `Playing` |
| HUD 未接数据 | 把 `HUDCanvas` 放进 Game 场景但不赋三个数据引用 → Play | 一条「HUD 初始化失败」Warning；波次行不刷新；**不应**每帧刷 NRE |
| HUD 数据晚于 HUD 启用 | 接好三个引用，但让玩家数据在 HUD 启用之后才 `Initialize` | 数据一就绪，界面立刻显示当前 HP/EXP/LV/属性/波次（靠 `Update` 重试），无需等第一次掉血 |
| HUD 已接数据 | 按 4.1 赋好三个引用 → Play | 开局立即显示 `HP: 100/100`、`EXP: 0/30`、`LV: 1`、三项属性、`WaveIndex: --`、`WaveTime: --` |
| 隐藏再显示 | Play 中把 `HUDCanvas` 手动 `SetActive(false)` 再 `true`，然后改变 HP/经验 | 应继续刷新（订阅在 `OnEnable`，成对复位） |
| 波间显示 | 让一波结束、下一波未开始时观察 | 应显示 `WaveIndex: --` + `WaveTime: --`，不会提前跳到下一波编号 |
| 倒计时 | 普通波进行中观察 `WaveTimeText` | 显示 `WaveTime: 12.3` 形式，0.1 秒精度 |
| 玩家死亡 | 把玩家 HP 打到 0 | HUD 显示 `HP: 0/100`；不弹任何结算界面（结算归 Game System，尚未接线） |
| 暂停面板 | 把 PauseCanvas 设为激活 → Play → 点 ResumeButton | 面板隐藏；**游戏不会真正暂停**（预期内，0.4-2） |
| 结算 | 触发 Victory / GameOver | 当前没有任何界面（`SettlementCanvas` 未实例化且为空壳），属 2.5 未实现 |

---

## 六、与 GDD 的追踪表

| GDD 界面（第 420–429 行） | 本模块实现 | 状态 |
|---|---|---|
| Main Menu | `MainMenuCanvas` | 面板可用；开始请求已发；退出为假实现且 onClick 失效 |
| HUD（含波次、剩余时间、Boss 血条） | `HUDCanvas` + `HUD.cs` | 6 行状态 + 波次/时间已实现（含初始快照与波间处理）；Boss 血条、成长点、属性阶数**缺失**；数据引用待场景注入 |
| 属性升级界面 | — | **未实现**（P0 缺口） |
| Pause | `PauseCanvas` | 面板与按钮可用；暂停/恢复流程未接 |
| Game Over | `SettlementCanvas` | 空壳 |
| Victory | `SettlementCanvas` | 空壳 |

**建议的后续顺序**：① 修 0.4-1（退出按钮 onClick） → ② 与主程确认 1.3 的暂停/恢复/返回入口与 UI 显隐驱动方 → ③ 等 Game System 把玩家与 SpawnScheduler 接进 Game 场景，HUD 才能联调 → ④ 补属性升级界面（P0） → ⑤ 补结算界面 → ⑥ Boss 血条与成长点/阶数显示 → ⑦ 中文与图集优化（P1）。

---

## 七、主菜单交互

- **StartGameButton**：点击后直接触发 `GameManager.StartGame()`，用于替换 `DevelopmentLauncher` 面板上的开始方法。
- **ExitGameButton**：目前仅输出调试信息，暂无实际退出逻辑；建议 UI 只发送请求，真实退出逻辑待其他模块实现。
- **GameTitleText**：`Awake` 时把 `gameTitleText` 字段写入 TMP 文本，改标题只需改 Inspector 字段，不用改代码。
