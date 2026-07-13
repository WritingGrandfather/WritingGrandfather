using UnityEngine;

/// <summary>
/// WritingCell 영역만 잘라서 이미지(Texture2D / PNG)로 캡처한다.
/// 전용 직교(Orthographic) 카메라를 칸 위에 정확히 맞춰 RenderTexture로 렌더링하므로,
/// 배경이나 다른 UI에 영향받지 않고 유저가 그린 획만 깔끔하게 담긴다.
/// </summary>
public class CellCapture : MonoBehaviour
{
    [Tooltip("캡처 이미지 한 변의 해상도(px). 정사각 기준. 커질수록 인식 정확도↑, 용량↑")]
    public int resolution = 512;

    [Tooltip("캡처 배경색 (인식용으로 보통 흰색 권장)")]
    public Color backgroundColor = Color.white;

    [Tooltip("캡처할 레이어. 획(Line) 전용 레이어를 만들어 지정하면 배경/UI가 안 섞임. 기본은 전체.")]
    public LayerMask cullingMask = ~0;

    Camera captureCam;

    void Awake()
    {
        // 캡처 전용 카메라 생성 (평소엔 꺼둠 — Render() 호출 시에만 그림)
        var go = new GameObject("CellCaptureCamera");
        go.transform.SetParent(transform, false);
        captureCam = go.AddComponent<Camera>();
        captureCam.enabled = false;                 // 자동 렌더 방지, 수동 Render만 사용
        captureCam.orthographic = true;
        captureCam.clearFlags = CameraClearFlags.SolidColor;
    }

    /// <summary>
    /// 칸 영역을 캡처해 Texture2D로 반환한다. (RGB24)
    /// 반환된 텍스처는 호출자가 다 쓰면 Destroy 해야 메모리 누수가 없다.
    /// </summary>
    public Texture2D Capture(WritingCell cell)
    {
        Rect rect = cell.WorldRect;

        // 카메라를 칸 중심에 배치. 2D이므로 z만 뒤로 빼서 정면을 바라보게 함.
        captureCam.transform.position = new Vector3(rect.center.x, rect.center.y, -10f);
        captureCam.transform.rotation = Quaternion.identity;
        captureCam.orthographicSize = rect.height * 0.5f;   // 세로 절반이 orthographicSize
        captureCam.aspect = rect.width / rect.height;       // 칸 비율 그대로
        captureCam.backgroundColor = backgroundColor;
        captureCam.cullingMask = cullingMask;

        // 칸 비율에 맞춰 렌더 타겟 크기 결정 (긴 변 = resolution)
        int w = rect.width >= rect.height ? resolution : Mathf.RoundToInt(resolution * rect.width / rect.height);
        int h = rect.height >= rect.width ? resolution : Mathf.RoundToInt(resolution * rect.height / rect.width);
        w = Mathf.Max(1, w);
        h = Mathf.Max(1, h);

        RenderTexture rt = RenderTexture.GetTemporary(w, h, 16);
        RenderTexture prev = RenderTexture.active;

        captureCam.targetTexture = rt;
        captureCam.Render();

        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        // 정리
        captureCam.targetTexture = null;
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return tex;
    }

    /// <summary>칸 영역을 캡처해 PNG 바이트로 반환한다. (AI API 전송용)</summary>
    public byte[] CapturePng(WritingCell cell)
    {
        var tex = Capture(cell);
        byte[] png = tex.EncodeToPNG();
        Destroy(tex);
        return png;
    }
}
