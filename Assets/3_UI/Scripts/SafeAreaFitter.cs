using UnityEngine;
using UnityEngine.UIElements;

[ExecuteAlways]
[RequireComponent(typeof(UIDocument))]
public class SafeAreaFitter : MonoBehaviour
{
    [SerializeField] string targetElementName = "lobby-root";

    UIDocument _document;
    VisualElement _target;
    Rect _appliedSafeArea;
    Vector2Int _appliedScreenSize;

    void OnEnable()
    {
        _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;
        _target = string.IsNullOrEmpty(targetElementName) ? root : root.Q<VisualElement>(targetElementName);
        _target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        ApplySafeArea();
    }

    void OnDisable()
    {
        _target?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        ApplySafeArea();
    }

    void ApplySafeArea()
    {
        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);

        if (safeArea == _appliedSafeArea && screenSize == _appliedScreenSize)
            return;

        float panelWidth = _target.panel.visualTree.resolvedStyle.width;
        if (panelWidth <= 0f || screenSize.x <= 0)
            return;

        // PanelSettings matches width, so panel space always equals the reference
        // width; device-pixel-to-panel-unit scale is panelWidth / screenWidth.
        float scale = panelWidth / screenSize.x;

        _target.style.paddingLeft = safeArea.xMin * scale;
        _target.style.paddingRight = (screenSize.x - safeArea.xMax) * scale;
        _target.style.paddingTop = (screenSize.y - safeArea.yMax) * scale;
        _target.style.paddingBottom = safeArea.yMin * scale;

        _appliedSafeArea = safeArea;
        _appliedScreenSize = screenSize;
    }
}
