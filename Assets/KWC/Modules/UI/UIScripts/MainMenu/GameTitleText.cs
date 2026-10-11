using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace KWC
{
    public class GameTitleText : MonoBehaviour
    {
        [Header("设置的游戏标题文本")]
        public string gameTitleText = "Knight-WheelChair";
        private TMP_Text MainMenu_GameTitleText;

        void Awake()
        {
            MainMenu_GameTitleText = GetComponent<TMP_Text>();

            // 设置游戏标题Text组件的文本
            MainMenu_GameTitleText.text = gameTitleText; // 通过代码就可实现在游戏运行时动态更改文本
        }
    }
}
