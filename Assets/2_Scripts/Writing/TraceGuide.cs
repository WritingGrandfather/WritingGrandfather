using UnityEngine;

/// <summary>
/// 따라쓰기 본보기: 목표 글자를 칸(WritingCell) 위에 띄운다.
///
/// 글자를 폰트에서 텍스처로 구워 SpriteRenderer로 그린다.
/// (TextMesh는 URP 2D에서 렌더 순서 제어가 안 먹혀 획을 덮는 문제가 있음 —
///  스프라이트는 LineRenderer와 같은 정렬 규칙이라 sortingOrder로 확실히 뒤에 깔린다)
///
/// WritingCell.targetText가 바뀌면 자동으로 갱신된다. (외자 기준)
/// </summary>
public class TraceGuide : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("본보기를 띄울 칸")]
    [SerializeField] WritingCell cell;

    [Tooltip("본보기 폰트 (.ttf/.otf — 채점 기준 폰트와 같은 것 권장)")]
    [SerializeField] Font font;

    [Header("표시")]
    [Tooltip("본보기 색 (알파로 투명도 조절)")]
    [SerializeField] Color color = Color.white;

    [Tooltip("칸 높이 대비 글자 크기 비율")]
    [Range(0.2f, 1f)]
    [SerializeField] float fillRatio = 0.75f;

    [Tooltip("획보다 뒤로 밀어낼 z 거리")]
    [SerializeField] float zOffset = 0.5f;

    const int GlyphTexHeight = 256; // 구워낼 글자 텍스처 높이(px)
    const int FontBakeSize = 220;   // 폰트 아틀라스에 요청할 크기

    SpriteRenderer spriteRenderer;
    string lastText;

    void Awake()
    {
        var go = new GameObject("TraceGuideSprite");
        go.transform.SetParent(transform, false);
        spriteRenderer = go.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = -10; // 유저 획(기본 0)보다 항상 뒤
    }

    void Update()
    {
        if (cell == null || spriteRenderer == null) return;

        string target = (cell.targetText ?? "").Trim();
        if (target != lastText)
        {
            lastText = target;
            RebuildSprite(target);
        }

        spriteRenderer.color = color;

        if (spriteRenderer.sprite == null) return;

        // 칸 중앙 배치 + 칸 높이에 맞춰 크기 조정 (스프라이트는 세로 1유닛으로 구움)
        Rect rect = cell.WorldRect;
        float desired = rect.height * fillRatio;
        spriteRenderer.transform.position = new Vector3(rect.center.x, rect.center.y, zOffset);
        spriteRenderer.transform.localScale = Vector3.one * desired;
    }

    void RebuildSprite(string text)
    {
        if (spriteRenderer.sprite != null)
        {
            Destroy(spriteRenderer.sprite.texture);
            Destroy(spriteRenderer.sprite);
            spriteRenderer.sprite = null;
        }
        if (string.IsNullOrEmpty(text) || font == null) return;

        Texture2D tex = RenderGlyphTexture(text[0]);
        if (tex == null) return;

        // pixelsPerUnit = 텍스처 높이 → 스프라이트 세로 크기 1 유닛 (스케일로 최종 크기 조절)
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                   new Vector2(0.5f, 0.5f), tex.height);
        spriteRenderer.sprite = sprite;
    }

    /// <summary>폰트 아틀라스에서 글자를 읽어 흰색+알파 텍스처로 굽는다.</summary>
    Texture2D RenderGlyphTexture(char ch)
    {
        font.RequestCharactersInTexture(ch.ToString(), FontBakeSize, FontStyle.Normal);
        if (!font.GetCharacterInfo(ch, out CharacterInfo ci, FontBakeSize))
        {
            Debug.LogWarning($"[TraceGuide] 폰트에서 '{ch}' 글자를 찾지 못했습니다.");
            return null;
        }

        // 아틀라스는 CPU에서 못 읽으므로 RT로 복사 후 ReadPixels
        Texture atlas = font.material.mainTexture;
        RenderTexture rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0);
        Graphics.Blit(atlas, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var atlasTex = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
        atlasTex.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
        atlasTex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        int h = GlyphTexHeight;
        int w = Mathf.Max(8, Mathf.RoundToInt(h * (float)ci.glyphWidth / Mathf.Max(1, ci.glyphHeight)));

        // 잉크가 알파 채널인지 휘도인지 자동 판별
        float aFrac = SampleCoverage(atlasTex, ci, w, h, useAlpha: true);
        bool useAlpha = aFrac > 0.02f && aFrac < 0.6f;

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float ink = SampleGlyphInk(atlasTex, ci, (x + 0.5f) / w, (y + 0.5f) / h, useAlpha);
                // 텍스처 y는 아래→위, 글자 v는 위→아래이므로 뒤집어 저장
                tex.SetPixel(x, h - 1 - y, new Color(1f, 1f, 1f, ink));
            }
        }
        tex.Apply();
        Destroy(atlasTex);
        return tex;
    }

    static float SampleGlyphInk(Texture2D atlasTex, CharacterInfo ci, float u, float v, bool useAlpha)
    {
        // 글자 사각형을 이중선형 보간 (uv 회전/뒤집힘 자동 처리). v: 0=위
        Vector2 top = Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, u);
        Vector2 bottom = Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, u);
        Vector2 uv = Vector2.Lerp(top, bottom, v);
        Color c = atlasTex.GetPixelBilinear(uv.x, uv.y);
        return useAlpha ? c.a : (c.r + c.g + c.b) / 3f;
    }

    static float SampleCoverage(Texture2D atlasTex, CharacterInfo ci, int w, int h, bool useAlpha)
    {
        int hits = 0, total = 0;
        for (int y = 0; y < h; y += 8)
            for (int x = 0; x < w; x += 8)
            {
                total++;
                if (SampleGlyphInk(atlasTex, ci, (x + 0.5f) / w, (y + 0.5f) / h, useAlpha) > 0.35f) hits++;
            }
        return total > 0 ? (float)hits / total : 0f;
    }

    /// <summary>본보기 숨기기/보이기 (난이도 높은 스테이지에서 끄는 용도)</summary>
    public void SetVisible(bool visible)
    {
        if (spriteRenderer != null) spriteRenderer.enabled = visible;
    }

    /// <summary>화면에 실제 렌더된 본보기의 월드 영역. 채점기가 이 위치에 맞춰 비교한다.</summary>
    public bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        if (spriteRenderer == null || spriteRenderer.sprite == null) return false;
        bounds = spriteRenderer.bounds;
        return bounds.size.x > 0.0001f && bounds.size.y > 0.0001f;
    }
}
