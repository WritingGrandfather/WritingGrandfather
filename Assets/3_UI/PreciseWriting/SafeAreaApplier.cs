using UnityEngine;
using UnityEngine.UIElements;

namespace WritingGrandfather.UI.PreciseWriting
{
    /// <summary>
    /// Screen.safeArea 를 safe-area 요소의 inset(top/right/bottom/left)으로 적용.
    /// padding 을 쓰면 absolute 자식의 contentRect 높이가 0 으로 깨질 수 있음.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-100)]
    public class SafeAreaApplier : MonoBehaviour
    {
        private VisualElement safeArea;
        private bool applying;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            safeArea = root.Q("safe-area");
            if (safeArea == null)
            {
                Debug.LogWarning("SafeAreaApplier: 'safe-area' 없음", this);
                return;
            }

            safeArea.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (!applying) Apply();
            });
            Apply();
        }

        private void Apply()
        {
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;

            var panel = safeArea.panel;
            if (panel == null) return;

            var rootEl = safeArea.parent;
            if (rootEl == null) return;

            var rootW = rootEl.resolvedStyle.width;
            var rootH = rootEl.resolvedStyle.height;
            if (rootW <= 1f || rootH <= 1f)
            {
                rootW = rootEl.layout.width;
                rootH = rootEl.layout.height;
            }
            if (rootW <= 1f || rootH <= 1f) return;

            var sa = Screen.safeArea;
            var top = (Screen.height - sa.yMax) / Screen.height * rootH;
            var bottom = sa.yMin / Screen.height * rootH;
            var left = sa.xMin / Screen.width * rootW;
            var right = (Screen.width - sa.xMax) / Screen.width * rootW;

            applying = true;
            safeArea.style.paddingTop = 0;
            safeArea.style.paddingBottom = 0;
            safeArea.style.paddingLeft = 0;
            safeArea.style.paddingRight = 0;
            safeArea.style.top = top;
            safeArea.style.bottom = bottom;
            safeArea.style.left = left;
            safeArea.style.right = right;
            applying = false;
        }
    }
}
