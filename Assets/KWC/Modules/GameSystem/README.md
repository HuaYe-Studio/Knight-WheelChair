# Game System · 王若兰

本目录维护 GameManager、Spawn/波次、对象池、配置类型、存档和流程接线。主程当前只搭公共骨架，后续内部实现由本模块 Owner 负责。

- `GameManager`：场景级状态及事件；没有单例、波次循环或胜负仲裁实现。
- `DevelopmentLauncher`：临时工程启动面板，正式UI接入时移除。
- `PrefabPool`：每Prefab一个组件，Rent初始化后启用；ReturnAll只清理，不是击杀。
- `Configuration`：SO类型；实例位于 `Assets/KWC/Data`，GameConfig组合引用。

普通波次30秒或全部计划敌人生成并击杀后提前结束；Boss波无时限。波末清理残留/预警并请求Combat补满HP，再打开升级。Wave 10首批出Boss，每批2指E1。

先确认最小出生距离、首批时间、尾批处理、找不到出生点、Wave10约20的确切上限；不要绕过 `TryGetActualSpawnCount` 的 false。暂停/升级的子系统许可、同时死亡优先级、最终升级、保存字段/时机、池容量/不足策略和性能指标也尚未决定。
