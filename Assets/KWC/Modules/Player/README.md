# Player · 陈柏翰

本目录放 Input System、八方向移动、朝向、Camera/Cinemachine 代码；Prefab 交付到 `Assets/KWC/Prefabs/Player`。

实现 `KWC.Core.IPlayerContext`，通过注入的 `IPlayerStats.PlayerMovement` 读取 Combat 当前速度。攻击、HP、经验与成长都由 Combat 负责。响应 Game System 的控制许可，不自行判定胜负。

GDD 碰撞体尺寸为1×1×1。按键、斜向速度、物理类型/移动平面、朝向及相机方案待确认。暂停/Upgrade 控制策略由 Game System 统一协调；不在 Player 私自改 TimeScale。
