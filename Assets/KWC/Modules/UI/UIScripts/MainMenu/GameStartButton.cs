using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI; // 如果需要使用Unity中UI相关的组件、方法，需要引用此命名空间

namespace KWC
{
    public class StartGameButton : MonoBehaviour
    {
        /// <summary>
        /// 此脚本挂载于 MainMenu 中UI画布上负责 “开始游戏” 的按钮上，游戏运行时当此按钮被点击，应发出“开始游戏”的请求
        /// </summary>
        
        // 按钮组件
        private Button MainMenu_StartGameButton;

        void Awake()
        {
            // 获取对象本身的按钮（Button）组件
            MainMenu_StartGameButton = GetComponent<Button>();
        }
        
        void Start()
        {
            // 为按钮组件添加监听器
            MainMenu_StartGameButton.onClick.AddListener(SendStartGameRequest);
        }

        #region 发送“开始游戏”请求
        void SendStartGameRequest()
        {
            // 发送请求逻辑
            Debug.Log("“开始游戏”按键被点击，已发送“开始游戏”请求。");
        }
        #endregion
    }
}
