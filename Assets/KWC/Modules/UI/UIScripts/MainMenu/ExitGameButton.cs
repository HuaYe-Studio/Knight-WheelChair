using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KWC.UI
{
    public class ExitGameButton : MonoBehaviour
    {
        /// <summary>
        /// 此脚本挂载于负责 “退出游戏”按钮 的对象自身上，用于发起 “退出游戏” 的请求
        /// </summary>
        
        private Button MainMenu_ExitGameButton;

        void Awake()
        {
            MainMenu_ExitGameButton = GetComponent<Button>();
        }

        #region 公共方法：发送 “退出游戏” 请求
        public void SendExitGameRequest()
        {
            // 发送 “退出游戏” 请求逻辑
            Debug.Log("“退出游戏”按钮被按下，已发送“退出游戏”请求。");
        }
        #endregion
    }
}
