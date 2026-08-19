using UnityEngine;
using UnityEngine.UI;

namespace Game_UIFramework
{
    public class ExampleWindow : BaseWindow
    {
        public static readonly WindowKey<ExampleWindow> Key =
            new WindowKey<ExampleWindow>("ExampleWindow");

        public TMPro.TextMeshProUGUI TxtBefore;
        public TMPro.TextMeshProUGUI TxtAfter;
        public TMPro.TextMeshProUGUI TxtDesc;

        public void Open()
        {
            OpenInternal(() =>
            {
                TxtDesc.SetText("Hello! My Name Is JinHyung");
                TxtBefore.SetText("Open ExampleWindow Before");
            }, () =>
            {
                TxtAfter.SetText("Open ExampleWindow After");
            });
        }

        public void ChangeTextDescValue(string txtValue)
        {
            TxtDesc.SetText(txtValue);
        }

        protected override void OnClose()
        {
            base.OnClose();
        }
    }
}
