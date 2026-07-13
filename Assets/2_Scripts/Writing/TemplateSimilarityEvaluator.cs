using System;
using UnityEngine;

/// <summary>
/// 폰트로 그린 "본보기 글자"와 유저 글씨의 픽셀 유사도를 직접 비교해 채점하는 로컬 평가기.
/// (따라쓰기 교본 방식 — AI/네트워크 불필요, 즉시·무료·항상 같은 결과)
///
/// 원리:
///  1) 목표 글자를 폰트 아틀라스에서 꺼내 비트마스크로 만든다
///  2) 유저 잉크(PNG)도 같은 규격의 비트마스크로 만든다 (둘 다 꽉 차게 정규화)
///  3) 서로 겹치는 정도(F1: 정밀도×재현율)를 점수로 환산. 허용 오차만큼 굵힘 처리해 떨림 보정
///
/// 사용법: 이 컴포넌트를 추가하고 WritingFeedbackController의 evaluator에 연결.
/// </summary>
public class TemplateSimilarityEvaluator : HandwritingEvaluator
{
    [Header("참조 (따라쓰기 정렬 채점 — 둘 다 연결하면 '그 자리에 썼는지'까지 본다)")]
    [Tooltip("유저 획을 읽을 StrokeCapture")]
    [SerializeField] StrokeCapture strokeCapture;

    [Tooltip("본보기가 표시되는 칸")]
    [SerializeField] WritingCell cell;

    [Tooltip("화면의 본보기(TraceGuide). 연결하면 채점 위치·크기를 화면 표시와 자동으로 일치시킨다 (강력 추천)")]
    [SerializeField] TraceGuide traceGuide;

    [Header("본보기")]
    [Tooltip("본보기 글자를 그릴 폰트 (.ttf/.otf — TraceGuide에 쓰는 폰트와 반드시 같아야 함)")]
    [SerializeField] Font font;

    [Tooltip("칸 높이 대비 본보기 잉크 높이 (TraceGuide의 Fill Ratio×0.72 정도가 기준. last_compare.png 보면서 맞추기)")]
    [Range(0.2f, 1f)]
    [SerializeField] float templateHeightRatio = 0.55f;

    [Tooltip("본보기 세로 위치 미세 조정 (칸 높이 비율, +면 아래로)")]
    [Range(-0.2f, 0.2f)]
    [SerializeField] float templateOffsetY = 0f;

    [Tooltip("폰트 아틀라스에 요청할 글자 크기(px). 클수록 정밀")]
    [SerializeField] int fontRenderSize = 192;

    [Header("채점")]
    [Tooltip("비교 격자 해상도")]
    [SerializeField] int gridSize = 96;

    [Tooltip("허용 오차 (격자 대비 비율). 클수록 떨리고 어긋난 글씨에 관대")]
    [Range(0f, 0.2f)]
    [SerializeField] float tolerance = 0.08f;

    [Tooltip("벗어난 잉크(본보기 밖에 그린 획)를 얼마나 엄하게 깎을지. 1=동등, 2=두 배 가중")]
    [Range(1f, 3f)]
    [SerializeField] float strayPenalty = 2f;

    [Tooltip("벗어남 관용 구간 — 벗어난 잉크 비율이 이 값보다 작으면 감점이 완만하고, 넘어서면 급격히 커진다")]
    [Range(0.1f, 0.6f)]
    [SerializeField] float strayForgiveness = 0.35f;

    [Header("정자 검사 (흘림체 차단)")]
    [Tooltip("켜면 획수 부족(이어 쓰기)과 구불거리는 획(흘림)을 감점한다")]
    [SerializeField] bool requireNeatWriting = true;

    [Header("디버그")]
    [Tooltip("켜면 본보기/유저 마스크 비교 이미지를 DebugCapture 폴더에 저장")]
    [SerializeField] bool debugDump = true;

    public override void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        try
        {
            onComplete?.Invoke(EvaluateSync(request));
        }
        catch (Exception e)
        {
            Debug.LogError("[TemplateEval] " + e);
            onComplete?.Invoke(HandwritingFeedback.Error("채점 오류: " + e.Message));
        }
    }

    HandwritingFeedback EvaluateSync(HandwritingEvaluationRequest request)
    {
        string target = (request.targetText ?? "").Trim();
        if (string.IsNullOrEmpty(target))
            return HandwritingFeedback.Error("목표 글자가 없습니다.");
        if (target.Length != 1)
            return HandwritingFeedback.Error("따라쓰기 채점은 외자(한 글자)만 지원합니다.");
        if (font == null)
            return HandwritingFeedback.Error("TemplateSimilarityEvaluator에 폰트가 연결되지 않았습니다.");

        bool aligned = strokeCapture != null && cell != null; // 따라쓰기 정렬 채점 모드

        bool[,] user;
        int outsideInk = 0; // 칸 밖에 그린 잉크 (전부 감점 대상)
        System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> cellStrokes = null;
        if (aligned)
        {
            cellStrokes = strokeCapture.GetCellNormalizedStrokes(cell);
            user = RasterizeCellStrokes(cellStrokes, out outsideInk);
        }
        else
        {
            user = Normalize(MaskFromPng(request.imagePng));
        }

        if (CountInk(user) == 0 && outsideInk == 0)
            return new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = "글씨가 없어요. 칸에 글자를 써볼까요?" };

        bool[,] templ = aligned
            ? PlaceTemplate(MaskFromFont(target[0]))
            : Normalize(MaskFromFont(target[0]));
        if (CountInk(templ) == 0)
            return HandwritingFeedback.Error($"폰트에서 '{target}' 글자를 만들지 못했습니다.");

        // 교정 겹쳐보기용으로 마지막 비교 마스크 보관
        lastUserMask = user;
        lastTemplMask = templ;

        int score = Compare(user, templ, outsideInk);

        // 정자 검사: 흘림체(획 이어 쓰기, 구불거림)면 감점
        string neatWarn = null;
        if (requireNeatWriting && cellStrokes != null)
            score = ApplyNeatnessChecks(cellStrokes, target[0], score, out neatWarn);

        if (debugDump) DumpMasks(user, templ);

        return new HandwritingFeedback
        {
            recognizedText = target, // 유사도 방식이므로 목표 글자 기준으로 채점 (통과 여부는 컨트롤러가 점수로 판단)
            score = score,
            passed = false,
            message = neatWarn ?? $"본보기 글자와 {score}% 닮았어요.",
        };
    }

    // ── 교정 겹쳐보기: 마지막 평가의 비교 결과를 색깔 텍스처로 만든다 ──────
    //    잘 쓴 잉크=초록, 벗어난 잉크=빨강, 빠뜨린 본보기 부분=파랑, 나머지=투명
    bool[,] lastUserMask, lastTemplMask;

    public Texture2D BuildCompareTexture()
    {
        if (lastUserMask == null || lastTemplMask == null) return null;

        int n = gridSize;
        int r = Mathf.Max(1, Mathf.RoundToInt(tolerance * n));
        bool[,] userFat = Dilate(lastUserMask, r);
        bool[,] templFat = Dilate(lastTemplMask, Mathf.Max(1, r / 2));

        var ok = new Color(0.16f, 0.62f, 0.24f);      // 본보기 위에 잘 쓴 잉크
        var stray = new Color(0.85f, 0.20f, 0.15f);   // 벗어난 잉크
        var missed = new Color(0.25f, 0.45f, 0.95f, 0.85f); // 못 덮은 본보기 부분
        var clear = new Color(0f, 0f, 0f, 0f);

        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                bool u = lastUserMask[y, x], t = lastTemplMask[y, x];
                Color c = u && templFat[y, x] ? ok
                        : u ? stray
                        : t && !userFat[y, x] ? missed
                        : clear;
                tex.SetPixel(x, n - 1 - y, c); // 마스크는 top-down → 텍스처 좌표로 뒤집기
            }
        }
        tex.Apply();
        return tex;
    }

    // ── 채움 비율: 본보기 글자를 얼마나 덮었는지(재현율)만 계산 ──────────
    //    벗어난 잉크는 무시. 자동 판정 트리거용 — 매 프레임 호출해도 되도록 본보기 마스크는 캐시.
    char coverageChar;
    bool[,] coverageTemplMask;

    /// <summary>본보기 글자가 덮인 비율 0~1. (정규화 모양 비교 기준, 벗어난 잉크 무시)</summary>
    public float CoverageRatio(System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> normStrokes, char targetChar)
    {
        if (font == null || normStrokes == null || normStrokes.Count == 0) return 0f;

        if (coverageTemplMask == null || coverageChar != targetChar)
        {
            coverageChar = targetChar;
            coverageTemplMask = Normalize(MaskFromFont(targetChar));
        }
        int templInk = CountInk(coverageTemplMask);
        if (templInk == 0) return 0f;

        bool[,] user = Normalize(RasterizeNormStrokes(normStrokes));
        if (CountInk(user) == 0) return 0f;

        int r = Mathf.Max(1, Mathf.RoundToInt(tolerance * gridSize));
        bool[,] userFat = Dilate(user, r);

        int hit = 0;
        for (int y = 0; y < gridSize; y++)
            for (int x = 0; x < gridSize; x++)
                if (coverageTemplMask[y, x] && userFat[y, x]) hit++;

        return (float)hit / templInk;
    }

    // 0~1 정규화 획들을 격자에 굽는다 (채움 비율 계산용)
    bool[,] RasterizeNormStrokes(System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> strokes)
    {
        var mask = new bool[gridSize, gridSize];
        foreach (var stroke in strokes)
        {
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 a = stroke[i - 1] * (gridSize - 1);
                Vector2 b = stroke[i] * (gridSize - 1);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (float)s / steps);
                    int x = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, gridSize - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, gridSize - 1);
                    mask[y, x] = true;
                }
            }
        }
        return mask;
    }

    // ── 정자 검사: 획수 부족(이어 쓰기)과 구불거리는 획(흘림)을 감점 ──────
    //    (낙하 모드 등 외부에서도 쓸 수 있게 public static)
    public static int ApplyNeatnessChecks(System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> strokes,
                            char targetChar, int score, out string warn)
    {
        warn = null;
        float mult = 1f;

        // 1) 획수 검사 — 이어 쓰면 예상보다 획이 모자란다
        int expected = 0;
        if (HangulComposer.Decompose(targetChar, out char cho, out char jung, out char jong))
        {
            expected = HangulComposer.JamoStrokeCount(cho) + HangulComposer.JamoStrokeCount(jung)
                     + (jong != '\0' ? HangulComposer.JamoStrokeCount(jong) : 0);
        }

        int actual = strokes.Count;
        if (expected > 0 && actual < expected)
        {
            mult *= Mathf.Pow(0.55f, expected - actual); // 모자란 획 하나당 큰 감점
            warn = $"획을 이어서 쓰신 것 같아요. 한 획씩 또박또박 써볼까요? (약 {expected}획)";
        }
        else if (expected > 0 && actual > expected + 2)
        {
            mult *= 0.7f;
            warn = "획 수가 너무 많아요. 차분히 다시 써볼까요?";
        }

        // 2) 흘림 검사 — 직선/한 번 꺾임이어야 할 획이 심하게 구불거리면 감점
        int wiggly = 0;
        foreach (var s in strokes)
        {
            float len = PathLength(s);
            if (len < 0.03f || s.Count < 3) continue;
            float chord = Vector2.Distance(s[0], s[s.Count - 1]);

            if (chord < 0.35f * len)
            {
                // 제자리로 돌아오는 획: 면적이 있으면 동그라미(ㅇ, 정상), 없으면 갈지자(흘림)
                float area = Mathf.Abs(PolygonArea(s));
                if (area / (len * len) < 0.04f) wiggly++;
            }
            else if (CountTurns(s) > 2)
            {
                wiggly++;
            }
        }
        if (wiggly > 0)
        {
            mult *= Mathf.Pow(0.6f, wiggly);
            if (warn == null) warn = "선이 많이 구불거려요. 천천히 곧게 그어볼까요?";
        }

        int adjusted = Mathf.RoundToInt(score * mult);
        if (mult < 1f)
            Debug.Log($"[TemplateEval] 정자 검사: 획수 {actual}/{expected}, 흘림 획 {wiggly}개 → {score}점 → {adjusted}점");
        return adjusted;
    }

    static float PathLength(System.Collections.Generic.List<Vector2> pts)
    {
        float len = 0f;
        for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
        return len;
    }

    // 신발끈 공식 (닫힌 곡선으로 보고 면적 계산 — 동그라미 판별용)
    static float PolygonArea(System.Collections.Generic.List<Vector2> pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i];
            Vector2 b = pts[(i + 1) % pts.Count];
            area += a.x * b.y - b.x * a.y;
        }
        return area * 0.5f;
    }

    // 진행 방향이 크게 꺾인 횟수 (누적 방향 기준이라 잔떨림은 무시)
    static int CountTurns(System.Collections.Generic.List<Vector2> pts)
    {
        int turns = 0;
        Vector2 segDir = Vector2.zero;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 d = pts[i] - pts[i - 1];
            if (d.magnitude < 0.008f) continue;

            if (segDir == Vector2.zero) { segDir = d.normalized; continue; }

            if (Vector2.Angle(segDir, d) >= 55f)
            {
                turns++;
                segDir = d.normalized;
            }
            else
            {
                segDir = Vector2.Lerp(segDir, d.normalized, 0.3f).normalized;
            }
        }
        return turns;
    }

    // ── 유저 잉크: PNG(흰 바탕 검은 획) → 비트마스크 ────────────────────
    bool[,] MaskFromPng(byte[] png)
    {
        var mask = new bool[gridSize, gridSize];
        if (png == null || png.Length == 0) return mask;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(png);

        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                float u = (x + 0.5f) / gridSize;
                float v = 1f - (y + 0.5f) / gridSize; // 텍스처는 아래→위이므로 뒤집어 top-down으로
                Color c = tex.GetPixelBilinear(u, v);
                mask[y, x] = (c.r + c.g + c.b) / 3f < 0.5f; // 어두우면 잉크
            }
        }
        Destroy(tex);
        return mask;
    }

    // ── 본보기: 폰트 아틀라스에서 글자 영역을 샘플링해 비트마스크 ────────
    bool[,] MaskFromFont(char ch)
    {
        font.RequestCharactersInTexture(ch.ToString(), fontRenderSize, FontStyle.Normal);
        if (!font.GetCharacterInfo(ch, out CharacterInfo ci, fontRenderSize))
            return new bool[gridSize, gridSize];

        // 아틀라스는 CPU에서 못 읽으므로 RT로 복사 후 ReadPixels
        Texture atlas = font.material.mainTexture;
        RenderTexture rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0);
        Graphics.Blit(atlas, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        // 글자 사각형(회전 대응)을 격자로 샘플링 — 알파/휘도 중 그럴듯한 채널 자동 선택
        bool[,] byAlpha = SampleGlyph(tex, ci, useAlpha: true);
        bool[,] byLuma = SampleGlyph(tex, ci, useAlpha: false);
        Destroy(tex);

        float aFrac = InkFraction(byAlpha);
        float lFrac = InkFraction(byLuma);
        // 글자의 잉크 비율은 보통 3~60% — 범위 안에 드는 채널을 채택
        bool aOk = aFrac > 0.02f && aFrac < 0.6f;
        bool lOk = lFrac > 0.02f && lFrac < 0.6f;
        Debug.Log($"[TemplateEval] 본보기 잉크 비율 — alpha: {aFrac:P0}, luma: {lFrac:P0}");

        if (aOk && lOk) // 둘 다 그럴듯하면 전형적인 글자 밀도(15%)에 가까운 쪽
            return Mathf.Abs(aFrac - 0.15f) <= Mathf.Abs(lFrac - 0.15f) ? byAlpha : byLuma;
        if (aOk) return byAlpha;
        if (lOk) return byLuma;

        // 둘 다 이상하면 실패 처리 (덩어리 마스크로 아무 글씨나 통과하는 것 방지)
        Debug.LogError("[TemplateEval] 폰트 아틀라스에서 글자를 읽지 못했습니다. (마스크가 비었거나 덩어리)");
        return new bool[gridSize, gridSize];
    }

    bool[,] SampleGlyph(Texture2D atlasTex, CharacterInfo ci, bool useAlpha)
    {
        var mask = new bool[gridSize, gridSize];
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                float u = (x + 0.5f) / gridSize;
                float v = (y + 0.5f) / gridSize; // 0=위
                // 글자 사각형을 이중선형 보간 (uv 회전/뒤집힘 자동 처리)
                Vector2 top = Vector2.Lerp(ci.uvTopLeft, ci.uvTopRight, u);
                Vector2 bottom = Vector2.Lerp(ci.uvBottomLeft, ci.uvBottomRight, u);
                Vector2 uv = Vector2.Lerp(top, bottom, v);
                Color c = atlasTex.GetPixelBilinear(uv.x, uv.y);
                float ink = useAlpha ? c.a : (c.r + c.g + c.b) / 3f;
                mask[y, x] = ink > 0.35f;
            }
        }
        return mask;
    }

    // ── 정렬 모드: 칸 좌표 획들을 격자에 굽는다 (칸 밖 잉크는 개수로 반환) ──
    bool[,] RasterizeCellStrokes(System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> strokes, out int outsideInk)
    {
        var mask = new bool[gridSize, gridSize];
        var outside = new System.Collections.Generic.HashSet<long>();

        foreach (var stroke in strokes)
        {
            for (int i = 1; i < stroke.Count; i++)
            {
                Vector2 a = stroke[i - 1] * gridSize;
                Vector2 b = stroke[i] * gridSize;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
                for (int s = 0; s <= steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (float)s / steps);
                    int x = Mathf.FloorToInt(p.x);
                    int y = Mathf.FloorToInt(p.y);
                    if (x >= 0 && x < gridSize && y >= 0 && y < gridSize)
                        mask[y, x] = true;
                    else
                        outside.Add((long)(y + 10000) * 100000 + (x + 10000)); // 칸 밖 셀 (중복 제거)
                }
            }
        }
        outsideInk = outside.Count;
        return mask;
    }

    // ── 정렬 모드: 본보기 글자를 화면 표시(TraceGuide)와 같은 위치·크기로 배치 ──
    bool[,] PlaceTemplate(bool[,] src)
    {
        int n = gridSize;
        int minX = n, minY = n, maxX = -1, maxY = -1;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (src[y, x])
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return src;

        float boxW = maxX - minX + 1;
        float boxH = maxY - minY + 1;

        // 배치 목표 영역 (그리드 단위)
        float dstX, dstY, dstW, dstH;
        if (traceGuide != null && cell != null && traceGuide.TryGetWorldBounds(out Bounds gb))
        {
            // 화면에 렌더된 본보기의 실제 영역을 칸 좌표로 변환 → 표시와 채점이 정확히 일치
            Rect cr = cell.WorldRect;
            dstX = (gb.min.x - cr.xMin) / cr.width * n;
            dstW = gb.size.x / cr.width * n;
            float topN = 1f - (gb.max.y - cr.yMin) / cr.height; // y 뒤집기 (위→아래)
            dstY = topN * n;
            dstH = gb.size.y / cr.height * n;
        }
        else
        {
            // 폴백: 칸 중앙, 지정 높이
            float scaleF = templateHeightRatio * n / boxH;
            dstW = boxW * scaleF;
            dstH = boxH * scaleF;
            dstX = (n - dstW) * 0.5f;
            dstY = (n - dstH) * 0.5f + templateOffsetY * n;
        }

        var dst = new bool[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // 목표 영역 → 글리프 bbox 역매핑
                float u = (x - dstX) / Mathf.Max(dstW, 0.001f);
                float v = (y - dstY) / Mathf.Max(dstH, 0.001f);
                if (u < 0f || u >= 1f || v < 0f || v >= 1f) continue;
                int sx = Mathf.RoundToInt(minX + u * (boxW - 1));
                int sy = Mathf.RoundToInt(minY + v * (boxH - 1));
                if (sx >= 0 && sx < n && sy >= 0 && sy < n)
                    dst[y, x] = src[sy, sx];
            }
        }
        return dst;
    }

    // ── 정규화: 잉크 바운딩 박스를 여백 12% 남기고 중앙에 꽉 차게 ───────
    bool[,] Normalize(bool[,] src)
    {
        int n = gridSize;
        int minX = n, minY = n, maxX = -1, maxY = -1;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (src[y, x])
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return src; // 잉크 없음

        float boxW = maxX - minX + 1;
        float boxH = maxY - minY + 1;
        float size = Mathf.Max(boxW, boxH);
        float margin = 0.12f * n;
        float scale = (n - 2f * margin) / size;
        float offX = (n - boxW * scale) * 0.5f;
        float offY = (n - boxH * scale) * 0.5f;

        var dst = new bool[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                // 역매핑으로 샘플
                int sx = Mathf.RoundToInt(minX + (x - offX) / scale);
                int sy = Mathf.RoundToInt(minY + (y - offY) / scale);
                if (sx >= 0 && sx < n && sy >= 0 && sy < n)
                    dst[y, x] = src[sy, sx];
            }
        }
        return dst;
    }

    // ── 비교: 서로 오차만큼 굵힌 뒤 정밀도/재현율 → 점수 ─────────────
    //   벗어난 잉크(정밀도)는 빠뜨림(재현율)보다 좁은 오차 + strayPenalty 가중으로 더 엄하게 깎는다.
    int Compare(bool[,] user, bool[,] templ, int outsideInk = 0)
    {
        int r = Mathf.Max(1, Mathf.RoundToInt(tolerance * gridSize));
        int rTight = Mathf.Max(1, r / 2); // 벗어남 판정은 더 빡빡하게
        bool[,] userFat = Dilate(user, r);
        bool[,] templFatTight = Dilate(templ, rTight);

        int userInk = 0, templInk = 0, userHit = 0, templHit = 0;
        for (int y = 0; y < gridSize; y++)
        {
            for (int x = 0; x < gridSize; x++)
            {
                if (user[y, x]) { userInk++; if (templFatTight[y, x]) userHit++; }
                if (templ[y, x]) { templInk++; if (userFat[y, x]) templHit++; }
            }
        }
        userInk += outsideInk; // 칸 밖 잉크는 전부 빗나간 것으로 계산
        if (userInk == 0 || templInk == 0) return 0;

        float precision = (float)userHit / userInk;  // 유저 잉크가 본보기 위에 있는가 (벗어남 감점)
        float recall = (float)templHit / templInk;   // 본보기를 얼마나 덮었는가 (빠뜨림 감점)

        // 벗어남 감점 곡선 (3차): 조금 벗어난 건 관대하게, 많이 벗어날수록 가속 감점.
        //   sf = 벗어난 잉크 비율. sf < strayForgiveness면 선형보다 훨씬 완만, 넘어서면 급격히 커진다.
        float sf = 1f - precision;
        float f = Mathf.Max(strayForgiveness, 0.01f);
        float pEff = Mathf.Clamp01(1f - (sf * sf * sf) / (f * f));

        // 가중 조화평균: strayPenalty만큼 정밀도(벗어남)에 무게
        float w = Mathf.Max(1f, strayPenalty);
        float denom = w / Mathf.Max(pEff, 1e-4f) + 1f / Mathf.Max(recall, 1e-4f);
        float score01 = (w + 1f) / denom;

        // 잉크 과다 사용 감점: 본보기보다 훨씬 많은 잉크(낙서로 도배)는 점수를 깎는다
        float inkRatio = (float)userInk / templInk;
        if (inkRatio > 1.5f)
            score01 *= (1.5f / inkRatio) * (1.5f / inkRatio);

        Debug.Log($"[TemplateEval] 덮음(재현율) {recall:P0}, 본보기 위(정밀도) {precision:P0} (곡선 적용 {pEff:P0}), " +
                  $"잉크 비율 {inkRatio:F1}배, 칸 밖 {outsideInk}칸 → {Mathf.RoundToInt(score01 * 100f)}점");
        return Mathf.RoundToInt(score01 * 100f);
    }

    // ── 디버그: 비교 상황을 이미지로 저장 (회색=본보기, 검정=유저, 초록=겹침) ──
    void DumpMasks(bool[,] user, bool[,] templ)
    {
        try
        {
            int n = gridSize, scale = 4;
            var tex = new Texture2D(n * scale, n * scale, TextureFormat.RGB24, false);
            for (int y = 0; y < n * scale; y++)
            {
                for (int x = 0; x < n * scale; x++)
                {
                    int gy = y / scale, gx = x / scale;
                    bool u = user[gy, gx], t = templ[gy, gx];
                    Color c = u && t ? new Color(0.1f, 0.6f, 0.1f)
                            : u ? Color.black
                            : t ? new Color(0.75f, 0.75f, 0.75f)
                            : Color.white;
                    tex.SetPixel(x, n * scale - 1 - y, c); // top-down → 텍스처 좌표
                }
            }
            tex.Apply();
            string dir = System.IO.Path.Combine(Application.dataPath, "../DebugCapture");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "last_compare.png"), tex.EncodeToPNG());
            Destroy(tex);
        }
        catch (Exception e) { Debug.LogWarning("[TemplateEval] 디버그 저장 실패: " + e.Message); }
    }

    static bool[,] Dilate(bool[,] src, int r)
    {
        int n = src.GetLength(0);
        var cur = (bool[,])src.Clone();
        for (int step = 0; step < r; step++)
        {
            var next = (bool[,])cur.Clone();
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (!cur[y, x])
                        next[y, x] = (x > 0 && cur[y, x - 1]) || (x < n - 1 && cur[y, x + 1]) ||
                                     (y > 0 && cur[y - 1, x]) || (y < n - 1 && cur[y + 1, x]);
            cur = next;
        }
        return cur;
    }

    static int CountInk(bool[,] m)
    {
        int n = m.GetLength(0), c = 0;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                if (m[y, x]) c++;
        return c;
    }

    static float InkFraction(bool[,] m)
    {
        int n = m.GetLength(0);
        return (float)CountInk(m) / (n * n);
    }
}
