using UnityEngine;

namespace Game_UIFramework
{
    public class ExampleUIManagment : BaseManagement
    {
        protected override void AddWindows()
        {
            RegisterWindow(ExampleWindow.Key, WindowType.Normal);
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.O))
            {
                OpenExampleWindow();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                CloseExampleWindow();
            }
        }

        public void OpenExampleWindow()
        {
            if (IsWindowOpen(ExampleWindow.Key))
            {
                var window = GetWindow(ExampleWindow.Key);
                window?.ChangeTextDescValue("JinHyung!! Is Already Open");
                return;
            }

            OpenWindow(ExampleWindow.Key,
                onOpenBefore: (window) =>
                {
                    window.ChangeTextDescValue("Hello! My Name Is JinHyung");
                },
                onOpenAfter: (window) =>
                {
                    Debug.Log("ExampleWindow opened successfully!");
                });
        }

        public void CloseExampleWindow()
        {
            CloseWindow(ExampleWindow.Key);
        }
    }
}
