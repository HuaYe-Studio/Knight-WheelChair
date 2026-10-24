using System.Collections;
using System.Collections.Generic;
using Microsoft.VisualBasic;
using UnityEngine;
using KWC.GameSystem;
using UnityEngine.UI;

namespace KWC.UI
{
    public class PausePanel : MonoBehaviour
    {
        #region 暂停界面UI控件
        [Header("暂停界面UI控件")]
        [Tooltip("继续游戏按钮")]
        public Button resumeGameButton;
        [Tooltip("游戏引导按钮")]
        public Button guideButton;
        [Tooltip("返回主菜单按钮")]
        public Button mainMenuButton;
        #endregion

        private GameManager gameManager;

        void Start()
        {
            gameManager = GameObject.Find("GameManager").GetComponent<GameManager>();
        }

        void OnEnable() 
        {
            // 先移除监听器防止反复添加
            resumeGameButton.onClick.RemoveListener(OnResumeBtnClick);
            guideButton.onClick.RemoveListener(OnGuideBtnClick);
            mainMenuButton.onClick.RemoveListener(OnMainMenuBtnClick);
            // 添加监听器
            resumeGameButton.onClick.AddListener(OnResumeBtnClick);
            guideButton.onClick.AddListener(OnGuideBtnClick);
            mainMenuButton.onClick.AddListener(OnMainMenuBtnClick);
        }

        void OnDisable()
        {
            resumeGameButton.onClick.RemoveListener(OnResumeBtnClick);
            guideButton.onClick.RemoveListener(OnGuideBtnClick);
            mainMenuButton.onClick.RemoveListener(OnMainMenuBtnClick);
        }

        #region 按钮事件
        void OnResumeBtnClick()
        {
            // 需要向 GameManager 请求继续游戏
            gameObject.SetActive(false);
        }

        void OnGuideBtnClick()
        {
            // 可以添加显示游戏教程的逻辑
        }

        void OnMainMenuBtnClick()
        {
            // 需要向 GameManager 请求返回主菜单
        }
        #endregion
    }
}
