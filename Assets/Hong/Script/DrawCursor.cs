using UnityEngine;
using UnityEngine.InputSystem;

// 마우스/터치 위치에 현재 브러시 또는 지우개 범위를 원으로 표시
// 드로우 모드 : lineWidth 기준 원
// 지우개 모드 : eraserRadius 기준 원
public class DrawCursor : MonoBehaviour
{
    public DrawLine drawLine;
    public Eraser   eraser;

    [SerializeField] int   circleSegments = 40;
    [SerializeField] float lineThickness  = 0.02f;

    LineRenderer lr;
    Camera       cam;

    void Awake()
    {
        cam = Camera.main;

        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop              = true;
        lr.positionCount     = circleSegments;
        lr.useWorldSpace     = true;
        lr.startWidth        = lineThickness;
        lr.endWidth          = lineThickness;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows    = false;

        // 회색 반투명 머티리얼 생성
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        lr.material = mat;

        // 물리 감지에 영향주지 않도록 레이어 설정 (선택사항 — 필요 시 Inspector에서 조정)
        gameObject.layer = LayerMask.NameToLayer("UI");
    }

    void Update()
    {
        if (Pointer.current == null) return;

        Vector2 worldPos = cam.ScreenToWorldPoint(Pointer.current.position.ReadValue());

        // 모드에 따라 반지름 결정
        float radius = drawLine.drawingEnabled
            ? drawLine.lineWidth * 0.5f
            : eraser.eraserRadius;

        UpdateCircle(worldPos, radius);
    }

    void UpdateCircle(Vector2 center, float radius)
    {
        for (int i = 0; i < circleSegments; i++)
        {
            float angle = 2f * Mathf.PI * i / circleSegments;
            lr.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                0f));
        }
    }
}
