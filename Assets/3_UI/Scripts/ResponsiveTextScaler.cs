using UnityEngine;
using UnityEngine.UIElements;

[ExecuteAlways]
[RequireComponent(typeof(UIDocument))]
public class ResponsiveTextScaler : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public string labelElementName;
        public string sizeReferenceElementName;
        public float baseFontSize;
        public float baseReferenceHeight;
    }

    [SerializeField] Entry[] entries;

    VisualElement[] _labels;
    VisualElement[] _references;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _labels = new VisualElement[entries.Length];
        _references = new VisualElement[entries.Length];

        for (int i = 0; i < entries.Length; i++)
        {
            _labels[i] = root.Q<VisualElement>(entries[i].labelElementName);
            _references[i] = string.IsNullOrEmpty(entries[i].sizeReferenceElementName)
                ? _labels[i]
                : root.Q<VisualElement>(entries[i].sizeReferenceElementName);

            _references[i]?.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        ApplyAll();
    }

    void OnDisable()
    {
        if (_references == null)
            return;

        foreach (var reference in _references)
            reference?.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
    }

    void OnGeometryChanged(GeometryChangedEvent evt)
    {
        ApplyAll();
    }

    public void Refresh()
    {
        ApplyAll();
    }

    void ApplyAll()
    {
        for (int i = 0; i < entries.Length; i++)
        {
            var label = _labels[i];
            var reference = _references[i];
            var entry = entries[i];

            if (label == null || reference == null || entry.baseReferenceHeight <= 0f)
                continue;

            float resolvedHeight = reference.resolvedStyle.height;
            if (resolvedHeight <= 0f)
                continue;

            float ratio = resolvedHeight / entry.baseReferenceHeight;
            label.style.fontSize = entry.baseFontSize * ratio;
        }
    }
}
