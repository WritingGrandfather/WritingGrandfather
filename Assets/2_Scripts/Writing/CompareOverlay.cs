using UnityEngine;

/// <summary>
/// 교정 겹쳐보기: 판정 직후 쓰기 칸 위에 비교 결과를 잠깐 띄운다.
///
///   초록 = 본보기 위에 잘 쓴 부분
///   빨강 = 본보기를 벗어난 부분
///   파랑 = 본보기인데 못 덮은(빠뜨린) 부분
///
/// 사용법: WritingCell 오브젝트에 추가하고 cell 연결.
///         판정하는 쪽(FallingWritingSession 등)에서 Show(texture) 호출.
/// </summary>
public class CompareOverlay : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("겹쳐보기를 띄울 칸")]
    [SerializeField] WritingCell cell;

    [Header("표시")]
    [Tooltip("표시 유지 시간(초)")]
    [SerializeField] float showDuration = 1.5f;

    [Tooltip("사라질 때 페이드 시간(초)")]
    [SerializeField] float fadeDuration = 0.4f;

    [Tooltip("칸 높이 대비 크기 비율 (TraceGuide의 Fill Ratio와 맞추면 본보기와 겹친다)")]
    [Range(0.2f, 1f)]
    [SerializeField] float fillRatio = 0.75f;

    [Tooltip("칸 중심 기준 세로 오프셋 (칸 높이 비율, +면 위) — 칸과 겹치지 않게 위에 띄운다")]
    [SerializeField] float yOffsetRatio = 1f;

    [Tooltip("정렬 순서 — 획(0)보다 높게 해서 맨 위에 보이게")]
    [SerializeField] int sortingOrder = 5;

    [Header("배경 카드 (외곽선 있는 반투명 사각형)")]
    [SerializeField] Color backgroundColor = Color.white; // 불투명 흰색 — 교정 이미지가 또렷하게 보이도록
    [SerializeField] Color borderColor = new Color(0.63f, 0.58f, 0.51f);

    [Tooltip("카드 크기 대비 테두리 두께 비율")]
    [Range(0.005f, 0.05f)]
    [SerializeField] float borderWidth = 0.018f;

    [Tooltip("비교 글자 대비 카드 크기 배율 (여백)")]
    [SerializeField] float backdropScale = 1.25f;

    SpriteRenderer sr;        // 비교 글자
    SpriteRenderer backdrop;  // 배경 카드
    float timer;
    bool showing;

    /// <summary>표시 시작부터 완전히 사라질 때까지 걸리는 시간(초) — 진행 대기용</summary>
    public float TotalDuration => showDuration + fadeDuration;

    void Awake()
    {
        // 배경 카드 (글자보다 한 단계 뒤)
        var bgGo = new GameObject("CompareOverlayCard");
        bgGo.transform.SetParent(transform, false);
        backdrop = bgGo.AddComponent<SpriteRenderer>();
        backdrop.sortingOrder = sortingOrder - 1;
        backdrop.sprite = BuildCardSprite();
        backdrop.enabled = false;

        var go = new GameObject("CompareOverlaySprite");
        go.transform.SetParent(transform, false);
        sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;
        sr.enabled = false;
    }

    // 외곽선 있는 반투명 카드 텍스처 (1×1 유닛, 스케일로 크기 조절)
    Sprite BuildCardSprite()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        Color32 bg = backgroundColor, bd = borderColor;
        int b = Mathf.Max(2, Mathf.RoundToInt(borderWidth * n));

        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = (x < b || x >= n - b || y < b || y >= n - b) ? bd : bg;

        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
    }

    /// <summary>비교 텍스처를 칸 위에 띄운다. (TemplateSimilarityEvaluator.BuildCompareTexture 결과)</summary>
    public void Show(Texture2D tex)
    {
        if (tex == null || cell == null || sr == null) return;

        // 이전 표시 정리
        if (sr.sprite != null)
        {
            Destroy(sr.sprite.texture);
            Destroy(sr.sprite);
        }

        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                   new Vector2(0.5f, 0.5f), tex.height); // 세로 1유닛
        sr.sprite = sprite;
        sr.color = Color.white;

        Rect rect = cell.WorldRect;
        float size = rect.height * fillRatio;
        var pos = new Vector3(rect.center.x, rect.center.y + rect.height * yOffsetRatio, 0f);

        sr.transform.position = pos;
        sr.transform.localScale = Vector3.one * size;

        backdrop.transform.position = pos;
        backdrop.transform.localScale = Vector3.one * (size * backdropScale);
        backdrop.color = Color.white;

        sr.enabled = true;
        backdrop.enabled = true;
        timer = 0f;
        showing = true;
    }

    void Update()
    {
        if (!showing) return;

        timer += Time.deltaTime;
        if (timer >= showDuration + fadeDuration)
        {
            sr.enabled = false;
            backdrop.enabled = false;
            showing = false;
            return;
        }
        if (timer > showDuration)
        {
            float a = 1f - (timer - showDuration) / fadeDuration;
            sr.color = new Color(1f, 1f, 1f, a);
            backdrop.color = new Color(1f, 1f, 1f, a);
        }
    }
}
