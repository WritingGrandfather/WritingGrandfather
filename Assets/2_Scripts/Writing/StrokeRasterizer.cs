using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 획 좌표 데이터를 직접 이미지로 그린다 (카메라 캡처 대체).
///
/// 카메라 캡처와 달리:
///  - 배경/테두리/UI가 절대 안 섞임 (순수 흰 바탕 + 검은 획)
///  - 글자 부분만 잘라내(bounding box) 중앙에 크게 배치 → 작게 쓴 글씨도 선명하게
///  - 획 굵기가 항상 일정 → AI 인식 정확도 최대화
/// </summary>
public static class StrokeRasterizer
{
    /// <summary>
    /// 정규화된 획들(0~1, y: 위→아래)을 PNG로 렌더링한다.
    /// </summary>
    /// <param name="strokes">StrokeCapture.GetNormalizedStrokes 결과</param>
    /// <param name="size">출력 이미지 한 변(px)</param>
    /// <param name="strokeWidth">획 굵기 (이미지 크기 대비 비율)</param>
    /// <param name="margin">글자 주변 여백 (이미지 크기 대비 비율)</param>
    public static byte[] ToPng(List<List<Vector2>> strokes, int size = 512, float strokeWidth = 0.04f, float margin = 0.12f)
    {
        var pixels = new Color32[size * size];
        var white = new Color32(255, 255, 255, 255);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

        // 잉크 영역(bounding box) 계산
        float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
        int pointCount = 0;
        foreach (var stroke in strokes)
        {
            foreach (var p in stroke)
            {
                xMin = Mathf.Min(xMin, p.x); xMax = Mathf.Max(xMax, p.x);
                yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
                pointCount++;
            }
        }

        if (pointCount > 0)
        {
            // 정사각 비율 유지: 긴 변 기준으로 스케일, 중앙 배치
            float boxW = Mathf.Max(xMax - xMin, 0.01f);
            float boxH = Mathf.Max(yMax - yMin, 0.01f);
            float maxDim = Mathf.Max(boxW, boxH);
            float scale = size * (1f - 2f * margin) / maxDim;
            float offX = (size - boxW * scale) * 0.5f;
            float offY = (size - boxH * scale) * 0.5f;

            int radius = Mathf.Max(1, Mathf.RoundToInt(strokeWidth * size * 0.5f));

            foreach (var stroke in strokes)
            {
                for (int i = 0; i < stroke.Count; i++)
                {
                    Vector2 a = MapPoint(stroke[i], xMin, yMin, scale, offX, offY);
                    StampCircle(pixels, size, a, radius);

                    if (i + 1 < stroke.Count)
                    {
                        Vector2 b = MapPoint(stroke[i + 1], xMin, yMin, scale, offX, offY);
                        // 선분을 따라 원을 찍어 굵은 선을 만든다
                        float dist = Vector2.Distance(a, b);
                        int steps = Mathf.CeilToInt(dist / Mathf.Max(1f, radius * 0.5f));
                        for (int s = 1; s <= steps; s++)
                            StampCircle(pixels, size, Vector2.Lerp(a, b, (float)s / steps), radius);
                    }
                }
            }
        }

        var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();
        Object.Destroy(tex);
        return png;
    }

    static Vector2 MapPoint(Vector2 p, float xMin, float yMin, float scale, float offX, float offY)
    {
        return new Vector2(offX + (p.x - xMin) * scale, offY + (p.y - yMin) * scale);
    }

    static void StampCircle(Color32[] pixels, int size, Vector2 center, int radius)
    {
        var black = new Color32(20, 20, 20, 255);
        int cx = Mathf.RoundToInt(center.x);
        int cy = Mathf.RoundToInt(center.y);
        int r2 = radius * radius;

        for (int dy = -radius; dy <= radius; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= size) continue;
            // 입력 y는 위→아래, 텍스처 y는 아래→위이므로 뒤집는다
            int row = (size - 1 - py) * size;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = cx + dx;
                if (px < 0 || px >= size) continue;
                if (dx * dx + dy * dy <= r2)
                    pixels[row + px] = black;
            }
        }
    }
}
