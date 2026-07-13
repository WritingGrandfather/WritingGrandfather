using System;
using UnityEngine;

/// <summary>
/// WritingCell 영역을 "쓰기 칸"으로 시각화하고, 필기를 칸 안으로 제한한다.
///
///  - 시각화: 미색 배경 + 테두리 + 십자 점선(가로/세로 중앙)을 런타임에 텍스처로 생성해
///            SpriteRenderer로 표시 (에셋 불필요, WritingPracticeScene의 칸과 같은 모양)
///  - 제한:   DrowLine.canDrawAt에 "칸 안인가?" 판정을 연결 → 칸 밖에서는
///            그리기 시작도 안 되고, 그리던 중 칸을 벗어나면 획이 이어지지 않는다.
///
/// 사용법: WritingCell이 있는 오브젝트에 추가하고 cell / drawLine만 연결.
/// </summary>
public class WritingCellBox : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("칸 영역 기준이 되는 WritingCell (비우면 같은 오브젝트에서 찾음)")]
    [SerializeField] WritingCell cell;

    [Tooltip("필기 제한을 걸 DrowLine")]
    [SerializeField] DrowLine drawLine;

    [Tooltip("끄면 시각화만 하고 필기 제한은 하지 않음")]
    [SerializeField] bool restrictDrawing = true;

    [Header("배치")]
    [Tooltip("켜면 칸(WritingCell)을 카메라 화면 맨 아래에 가로 중앙으로 붙인다")]
    [SerializeField] bool anchorToCameraBottom = true;

    [Tooltip("화면 아래 가장자리에서 띄울 여백 (월드 단위)")]
    [SerializeField] float bottomMargin = 0.1f;

    [Tooltip("칸 크기를 화면 너비 대비 비율로 자동 결정 (0이면 WritingCell.size 그대로 사용)")]
    [Range(0f, 1f)]
    [SerializeField] float widthAsScreenRatio = 0.8f;

    [Header("모양")]
    [SerializeField] Color backgroundColor = new Color(0.976f, 0.949f, 0.902f, 0.35f); // 미색, 반투명 (뒤 글자가 비쳐 보임)
    [SerializeField] Color borderColor = new Color(0.63f, 0.58f, 0.51f);
    [SerializeField] Color dotColor = new Color(0.72f, 0.68f, 0.62f);

    [Tooltip("테두리 두께 (칸 크기 대비 비율)")]
    [Range(0.002f, 0.05f)]
    [SerializeField] float borderWidth = 0.012f;

    [Tooltip("십자 점선 한 줄의 점 개수")]
    [SerializeField] int dotsPerLine = 15;

    [Tooltip("점 반지름 (칸 크기 대비 비율)")]
    [Range(0.002f, 0.02f)]
    [SerializeField] float dotRadius = 0.005f;

    [Tooltip("생성할 텍스처 해상도 (px)")]
    [SerializeField] int textureSize = 512;

    [Tooltip("스프라이트 정렬 순서 — 획(LineRenderer, 기본 0)보다 낮아야 칸이 뒤에 깔린다")]
    [SerializeField] int sortingOrder = -10;

    Camera cam;
    Texture2D tex;

    void Awake()
    {
        cam = Camera.main;
        if (cell == null) cell = GetComponent<WritingCell>();
        ApplyAnchor();
        BuildVisual();
    }

    void OnEnable()
    {
        if (restrictDrawing && drawLine != null)
            drawLine.canDrawAt = CanDrawAt;
    }

    void OnDisable()
    {
        // 내가 걸어둔 판정일 때만 해제 (다른 컨트롤러의 판정을 지우지 않도록)
        if (drawLine != null && drawLine.canDrawAt == (Func<Vector2, bool>)CanDrawAt)
            drawLine.canDrawAt = null;
    }

    void OnDestroy()
    {
        if (tex != null) Destroy(tex);
    }

    // ── 배치: 칸을 카메라 화면 맨 아래·가로 중앙에 붙인다 ──────────────
    void ApplyAnchor()
    {
        if (!anchorToCameraBottom || cell == null || cam == null || !cam.orthographic) return;

        // 칸 크기: 화면 너비 대비 비율로 정사각형 (WritingPracticeScene 느낌)
        if (widthAsScreenRatio > 0f)
        {
            float camWidth = 2f * cam.orthographicSize * cam.aspect;
            float s = camWidth * widthAsScreenRatio;
            cell.size = new Vector2(s, s);
        }

        float bottom = cam.transform.position.y - cam.orthographicSize;
        Vector3 p = cell.transform.position;
        p.x = cam.transform.position.x;                          // 가로 중앙
        p.y = bottom + bottomMargin + cell.size.y * 0.5f;        // 아래 가장자리 + 여백
        cell.transform.position = p;
    }

    /// <summary>화면 좌표가 칸 안인지 — DrowLine이 그리기 시작/진행 때마다 호출</summary>
    bool CanDrawAt(Vector2 screenPos)
    {
        if (cell == null) return true;
        if (cam == null) cam = Camera.main;
        if (cam == null) return true;

        Vector2 world = cam.ScreenToWorldPoint(screenPos);
        return cell.WorldRect.Contains(world);
    }

    // ── 시각화: 배경 + 테두리 + 십자 점선 텍스처를 만들어 자식 스프라이트로 표시 ──
    void BuildVisual()
    {
        if (cell == null)
        {
            Debug.LogError("[WritingCellBox] cell 참조가 없습니다.");
            return;
        }

        int n = Mathf.Clamp(textureSize, 64, 1024);
        tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };

        var px = new Color32[n * n];
        Color32 bg = backgroundColor, bd = borderColor, dc = dotColor;

        for (int i = 0; i < px.Length; i++) px[i] = bg;

        // 테두리
        int b = Mathf.Max(2, Mathf.RoundToInt(borderWidth * n));
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (x < b || x >= n - b || y < b || y >= n - b)
                    px[y * n + x] = bd;

        // 십자 점선 (가로/세로 중앙선)
        int r = Mathf.Max(1, Mathf.RoundToInt(dotRadius * n));
        int count = Mathf.Max(3, dotsPerLine);
        for (int i = 0; i < count; i++)
        {
            int p = Mathf.RoundToInt((i + 0.5f) / count * n);
            StampDot(px, n, p, n / 2, r, dc); // 가로 점선
            StampDot(px, n, n / 2, p, r, dc); // 세로 점선
        }

        tex.SetPixels32(px);
        tex.Apply();

        // 1×1 월드 크기 스프라이트로 만든 뒤 칸 크기로 스케일
        var sprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
        var go = new GameObject("CellBoxVisual");
        go.transform.SetParent(transform, false);
        go.transform.position = cell.Center;
        go.transform.localScale = new Vector3(cell.size.x, cell.size.y, 1f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = sortingOrder;

        Debug.Log($"[WritingCellBox] 생성 — 배경 알파 {backgroundColor.a:F2}, 칸 {cell.size}, " +
                  $"셰이더 '{(sr.sharedMaterial != null ? sr.sharedMaterial.shader.name : "없음")}'");

        // 디버그: 생성된 텍스처를 저장 (알파 채널 확인용)
        try
        {
            string dir = System.IO.Path.Combine(Application.dataPath, "../DebugCapture");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "cellbox.png"), tex.EncodeToPNG());
        }
        catch (System.Exception e) { Debug.LogWarning("[WritingCellBox] 디버그 저장 실패: " + e.Message); }
    }

    static void StampDot(Color32[] px, int n, int cx, int cy, int r, Color32 c)
    {
        for (int y = -r; y <= r; y++)
        {
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r * r) continue;
                int xx = cx + x, yy = cy + y;
                if (xx < 0 || xx >= n || yy < 0 || yy >= n) continue;
                px[yy * n + xx] = c;
            }
        }
    }
}
