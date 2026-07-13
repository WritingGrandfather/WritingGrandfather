using UnityEngine;
using UnityEngine.UIElements;

[ExecuteAlways]
[RequireComponent(typeof(UIDocument))]
public class SafeAreaFitter : MonoBehaviour
{
    [SerializeField] string targetElementName = "lobby-root";
    [SerializeField] float baseMargin = 32f;

    // Text elements that shrink together with the content box when SafeArea
    // insets eat into it, so text never gets clipped instead of just resized.
    // Matches the font-size values in Lobby.uss - keep both in sync.
    [SerializeField] string[] titleLabelNames = { "title-label-line1", "title-label-line2" };
    [SerializeField] float titleBaseFontSize = 52f;
    [SerializeField] string[] menuButtonNames = { "btn-start", "btn-settings", "btn-exit" };
    [SerializeField] float menuButtonBaseFontSize = 34f;
    // title-area (190) + its margin-bottom (40) + button-area (3 * (120 + 14)) in Lobby.uss.
    [SerializeField] float designContentHeight = 632f;
    // .title-area's own "padding: 0 40px" (both sides) in Lobby.uss.
    [SerializeField] float titleAreaHorizontalPadding = 80f;
    // .button-area's "width: 80%" in Lobby.uss.
    [SerializeField] float buttonAreaWidthFraction = 0.8f;
    [SerializeField] float minFontScale = 0.5f;

    UIDocument _document;
    VisualElement _target;
    TextElement[] _titleLabels;
    TextElement[] _menuButtons;
    Rect _appliedSafeArea;
    Vector2Int _appliedScreenSize;

    void OnEnable()
    {
        _document = GetComponent<UIDocument>();
        var root = _document.rootVisualElement;
        _target = string.IsNullOrEmpty(targetElementName) ? root : root.Q<VisualElement>(targetElementName);
        _target.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

        _titleLabels = System.Array.ConvertAll(titleLabelNames, name => _target.Q<TextElement>(name));
        _menuButtons = System.Array.ConvertAll(menuButtonNames, name => _target.Q<TextElement>(name));

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

    void Update()
    {
        // Device Simulator switching devices doesn't always raise a
        // GeometryChangedEvent, so poll Screen.safeArea directly as well.
        ApplySafeArea();
    }

    void ApplySafeArea()
    {
        if (_target == null || _target.panel == null)
            return;

        var safeArea = Screen.safeArea;
        var screenSize = new Vector2Int(Screen.width, Screen.height);

        if (safeArea == _appliedSafeArea && screenSize == _appliedScreenSize)
            return;

        var panel = _target.panel;
        float panelWidth = panel.visualTree.resolvedStyle.width;
        float panelHeight = panel.visualTree.resolvedStyle.height;
        if (panelWidth <= 0f || panelHeight <= 0f)
            return;

        // Let Unity's own screen-to-panel transform do the conversion instead of
        // re-deriving PanelSettings' scale factor by hand - it already accounts
        // for whichever ScreenMatchMode (Shrink/Expand/MatchWidthOrHeight) is
        // configured, so this stays correct no matter how that setting changes.
        // Screen.safeArea uses a bottom-left origin (like Input.mousePosition);
        // ScreenToPanel expects a top-left origin, so flip Y.
        Vector2 topLeft = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMin, screenSize.y - safeArea.yMax));
        Vector2 bottomRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMax, screenSize.y - safeArea.yMin));

        // Even with no notch/home-indicator inset, keep a minimum breathing
        // room from the screen edge so content doesn't hug it directly.
        float paddingLeft = Mathf.Max(0f, topLeft.x) + baseMargin;
        float paddingTop = Mathf.Max(0f, topLeft.y) + baseMargin;
        float paddingRight = Mathf.Max(0f, panelWidth - bottomRight.x) + baseMargin;
        float paddingBottom = Mathf.Max(0f, panelHeight - bottomRight.y) + baseMargin;
        _target.style.paddingLeft = paddingLeft;
        _target.style.paddingRight = paddingRight;
        _target.style.paddingTop = paddingTop;
        _target.style.paddingBottom = paddingBottom;

        // Only SafeArea-driven shrinkage should scale the text down - measured
        // against the panel's actual current size, not an assumed constant.
        float availableContentHeight = panelHeight - paddingTop - paddingBottom;
        float heightFontScale = Mathf.Clamp(availableContentHeight / designContentHeight, minFontScale, 1f);

        float contentWidth = panelWidth - paddingLeft - paddingRight;
        float titleAvailableWidth = Mathf.Max(0f, contentWidth - titleAreaHorizontalPadding);
        float buttonAvailableWidth = Mathf.Max(0f, contentWidth * buttonAreaWidthFraction);

        ApplyTextScale(_titleLabels, titleBaseFontSize, titleAvailableWidth, heightFontScale);
        ApplyTextScale(_menuButtons, menuButtonBaseFontSize, buttonAvailableWidth, heightFontScale);

        _appliedSafeArea = safeArea;
        _appliedScreenSize = screenSize;
    }

    void ApplyTextScale(TextElement[] elements, float baseFontSize, float availableWidth, float heightFontScale)
    {
        foreach (var element in elements)
        {
            if (element == null)
                continue;

            // Reset to the base size before measuring, otherwise a previous
            // frame's shrunk size would throw off this frame's measurement.
            element.style.fontSize = baseFontSize;
            var naturalSize = element.MeasureTextSize(element.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            float widthFontScale = naturalSize.x > 0f ? Mathf.Clamp01(availableWidth / naturalSize.x) : 1f;

            float fontScale = Mathf.Clamp(Mathf.Min(heightFontScale, widthFontScale), minFontScale, 1f);
            element.style.fontSize = baseFontSize * fontScale;
        }
    }
}
