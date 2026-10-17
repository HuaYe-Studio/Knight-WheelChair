# UI · 曾识宇

## 开发注意事项
本目录放 HUD、菜单、Pause/Upgrade/结算与反馈代码；Prefab 交付到 `Assets/KWC/Prefabs/UI`。

从 Combat 读取 HP、EXP、等级、成长点、属性，从 Game System 读取波次/计时/流程；Boss HP 来自 Combat。读当前快照后订阅事件，启停成对解绑。UI 只发请求，不扣血/扣点/刷怪。

TextMeshPro、UGUI和Input System已作为基础依赖。正式Canvas/EventSystem使用 InputSystemUIInputModule。默认 TMP 字体不覆盖中文，确定界面字体时选择可分发的中文字体并配置字体资产，不下载未授权素材。

当前 DevelopmentLauncher 是 Game System 的临时英文工程检查面板，正式UI接入后替换它。暂停键、布局、Upgrade关闭/确认方式、结算统计、返回菜单操作仍待确认。

## 主菜单UI
StartGameButton：点击后会直接触发对*GameManager*的*StartGame()*方法的调用，替换DevelopmentLauncher面板的开始方法；
ExitGameButton: 目前仅输出调试信息，暂无实际退出逻辑，建议UI仅发送请求，实际退出逻辑待其他模块实现。

