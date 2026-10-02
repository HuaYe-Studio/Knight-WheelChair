# Map · 吴钰涛

本目录放地图空间/碰撞代码。地图资源放 `Assets/KWC/Art/Map`，Prefab 交付到 `Assets/KWC/Prefabs/Map`，实际需要时再创建子目录。

交付地面、通行/阻挡事实、地图范围与有效位置查询；Game System 负责随机选点、距离检查和预警。墙体须能被 Enemy 检测，Dash 状态转换归 Enemy。

移动平面、2D/3D 物理、地图尺寸/边界/障碍/出生点、碰撞层尚未定。先与 Player、Enemy、Game System 及主程确认。当前 Game 是空集成场景，不表示已经交付地图。
