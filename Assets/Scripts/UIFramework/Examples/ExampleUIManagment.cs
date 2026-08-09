using UnityEngine;

namespace Game_UIFramework
{
    /// <summary>
    /// BaseManagement 사용 예제
    /// 각 콘텐츠별 Management는 BaseManagement를 상속받아 구현
    /// </summary>
    public class ExampleUIManagment : BaseManagement
    {
        /// <summary>
        /// 윈도우 등록 메서드
        /// Awake() 시 자동으로 호출되어 필요한 윈도우들을 등록
        /// </summary>
        protected override void AddWindows()
        {
            // 이 콘텐츠에서 사용할 윈도우들을 등록
            RegisterWindow(ExampleWindow.Key, WindowType.Normal);
        }

        void Update()
        {
            // 키보드 입력으로 윈도우 열기/닫기 테스트
            if (Input.GetKeyDown(KeyCode.O))
            {
                OpenExampleWindow();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                CloseExampleWindow();
            }
        }

        /// <summary>
        /// 예제 윈도우 열기
        /// </summary>
        public void OpenExampleWindow()
        {
            // 이미 열려있는지 확인
            if (IsWindowOpen(ExampleWindow.Key))
            {
                var window = GetWindow(ExampleWindow.Key);
                window?.ChangeTextDescValue("JinHyung!! Is Already Open");
                return;
            }

            // 윈도우 열기 (OpenInternal 대신 OpenWindow 사용)
            OpenWindow(ExampleWindow.Key,
                onOpenBefore: (window) =>
                {
                    // 윈도우가 열리기 전에 실행할 로직
                    window.ChangeTextDescValue("Hello! My Name Is JinHyung");
                },
                onOpenAfter: (window) =>
                {
                    // 윈도우가 열린 후에 실행할 로직
                    Debug.Log("ExampleWindow opened successfully!");
                });
        }

        /// <summary>
        /// 예제 윈도우 닫기
        /// </summary>
        public void CloseExampleWindow()
        {
            CloseWindow(ExampleWindow.Key);
        }
    }
}



