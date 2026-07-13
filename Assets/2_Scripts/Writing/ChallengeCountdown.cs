using System.Collections;
using UnityEngine;

/// <summary>
/// 도전 모드 시작 카운트다운: 씬에 들어오면 게임을 멈춘 채 화면 중앙에
/// 3 → 2 → 1 → "시작!"을 띄운 뒤 게임을 시작한다.
///
/// 사용법: 아무 오브젝트에 붙이고 spawner / session / drawLine / font 연결.
/// </summary>
public class ChallengeCountdown : MonoBehaviour
{
    [Header("참조 (카운트다운 동안 멈출 것들)")]
    [SerializeField] FallingWordSpawner spawner;
    [SerializeField] FallingWritingSession session;
    [SerializeField] DrowLine drawLine;

    [Tooltip("제한시간 컨트롤러 — 카운트다운 동안 시간이 줄지 않게 멈춘다")]
    [SerializeField] ChallengeSurvivalController survival;

    [Tooltip("본보기 초기화용 칸 — 카운트다운 동안 목표 글자를 비운다")]
    [SerializeField] WritingCell cell;

    [Header("표시")]
    [Tooltip("카운트다운 폰트 (레거시 다이내믹 폰트)")]
    [SerializeField] Font font;

    [Tooltip("몇부터 셀지")]
    [SerializeField] int countFrom = 3;

    [Tooltip("숫자 하나당 시간(초)")]
    [SerializeField] float stepDuration = 1f;

    [Tooltip("시작 문구 표시 시간(초)")]
    [SerializeField] float startTextDuration = 0.7f;

    [Tooltip("글자 높이 (화면 픽셀 기준)")]
    [SerializeField] int fontSizePx = 170;

    [SerializeField] Color textColor = new Color(0.93f, 0.42f, 0.18f); // 주황

    [Tooltip("정렬 순서 — 다른 모든 것 위에")]
    [SerializeField] int sortingOrder = 50;

    void Awake()
    {
        // 카운트다운 동안 게임 정지
        if (spawner != null) spawner.enabled = false;
        if (session != null) session.enabled = false;
        if (survival != null) survival.enabled = false; // 제한시간 정지
        if (drawLine != null) drawLine.drawingEnabled = false;
        if (cell != null) cell.targetText = "";         // 본보기 글자 비우기
    }

    IEnumerator Start()
    {
        Camera cam = Camera.main;

        var go = new GameObject("CountdownText");
        go.transform.SetParent(transform, false);
        if (cam != null)
            go.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, 0f);

        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 120;
        tm.color = textColor;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;

        // 화면 픽셀 기준 크기 → 월드 단위 환산 (줄 높이 ≈ fontSize × characterSize × 0.1)
        float wpp = cam != null && cam.pixelHeight > 0 ? 2f * cam.orthographicSize / cam.pixelHeight : 0.01f;
        tm.characterSize = fontSizePx * wpp * 10f / tm.fontSize;

        var mr = go.GetComponent<MeshRenderer>();
        if (font != null) mr.material = font.material;
        mr.sortingOrder = sortingOrder;

        for (int i = Mathf.Max(1, countFrom); i >= 1; i--)
        {
            tm.text = i.ToString();
            yield return new WaitForSeconds(stepDuration);
        }

        tm.text = LocalizationManager.Get("challenge.countdown_start");
        yield return new WaitForSeconds(startTextDuration);
        Destroy(go);

        // 게임 시작
        if (spawner != null) spawner.enabled = true;
        if (session != null) session.enabled = true;
        if (survival != null) survival.enabled = true;
        if (drawLine != null) drawLine.drawingEnabled = true;
    }
}
