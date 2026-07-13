using UnityEngine;
using TMPro;

/// <summary>
/// WritingFeedbackController의 이벤트(onFeedback / onStatus)를 받아 UI 텍스트에 표시하는 브릿지.
/// HandwritingFeedback 객체를 사람이 읽을 문자열로 바꿔 TMP 텍스트에 넣는다.
/// </summary>
public class FeedbackDisplayUI : MonoBehaviour
{
    [Tooltip("평가 결과(인식 글자/점수/피드백)를 표시할 텍스트")]
    public TMP_Text feedbackText;

    [Tooltip("진행 상태(예: '평가 중...')를 표시할 텍스트")]
    public TMP_Text statusText;

    [Tooltip("불통과(틀림)일 때 피드백 텍스트 색")]
    public Color failColor = new Color(0.86f, 0.15f, 0.15f);

    Color normalColor;
    bool normalColorCached;

    // onFeedback 이벤트에 연결
    public void OnFeedback(HandwritingFeedback fb)
    {
        if (feedbackText == null) return;

        if (!normalColorCached)
        {
            normalColor = feedbackText.color;
            normalColorCached = true;
        }

        if (fb == null)
        {
            feedbackText.text = "No result";
            feedbackText.color = failColor;
            return;
        }

        // 틀리면 빨간색으로 표시
        feedbackText.color = fb.passed ? normalColor : failColor;

        string pass = fb.passed ? "Pass" : "Retry";
        string stars = new string('★', fb.stars) + new string('☆', 3 - fb.stars);
        feedbackText.text =
            $"Recognized: {fb.recognizedText}\nScore: {fb.score} ({pass})\n{stars}\n\n{fb.message}";
    }

    // onStatus 이벤트에 연결
    public void OnStatus(string s)
    {
        if (statusText != null) statusText.text = s;
    }
}
