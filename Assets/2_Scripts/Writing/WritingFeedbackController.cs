using System;
using UnityEngine;
using UnityEngine.Events;

// UnityEvent<T> 제네릭은 인스펙터에 노출되지 않으므로 구체 서브클래스로 선언한다.
[Serializable] public class FeedbackEvent : UnityEvent<HandwritingFeedback> { }
[Serializable] public class StatusEvent : UnityEvent<string> { }

/// <summary>
/// 손글씨 피드백 전체 흐름을 조율하는 컨트롤러.
///
/// 흐름: [평가 버튼] → 칸 캡처(CellCapture) → 평가 요청(HandwritingEvaluator)
///       → 결과(HandwritingFeedback)를 UnityEvent로 UI에 전달
///
/// 씬 설정:
///  1) 이 컴포넌트를 빈 오브젝트에 추가
///  2) targetCell   : 평가할 WritingCell 지정
///  3) capture       : CellCapture 컴포넌트 지정
///  4) evaluator     : HandwritingEvaluator (지금은 MockHandwritingEvaluator) 지정
///  5) 버튼 OnClick → WritingFeedbackController.RequestFeedback
///  6) onFeedback / onStatus 이벤트를 TMP 텍스트 등에 연결
/// </summary>
public class WritingFeedbackController : MonoBehaviour
{
    [Header("참조")]
    [SerializeField] WritingCell targetCell;
    [SerializeField] StrokeCapture strokeCapture; // 획 좌표(획순/획수)
    [SerializeField] CellCapture imageCapture;     // PNG(글자 인식/모양)
    [SerializeField] HandwritingEvaluator evaluator;

    [Header("기획 판정 기준 (AI 프롬프트로 전달됨)")]
    [Tooltip("기획서에 정의된 채점/피드백 조건을 여기에 적는다. AI가 이 기준으로 판단.")]
    [TextArea(4, 12)]
    [SerializeField]
    string criteria = "";   // ← 기획 판정 기준을 나중에 여기 채우기 (비워둠)

    [Header("이벤트")]
    [Tooltip("평가 결과가 나오면 호출 (피드백 메시지 등 표시용)")]
    public FeedbackEvent onFeedback;

    [Tooltip("진행 상태 메시지 (예: '평가 중...') 표시용")]
    public StatusEvent onStatus;

    bool isEvaluating;

    /// <summary>평가 버튼에서 호출. 칸을 캡처해 AI 평가를 요청한다.</summary>
    public void RequestFeedback()
    {
        if (isEvaluating)
        {
            Debug.Log("[WritingFeedback] 이미 평가 중입니다.");
            return;
        }

        if (targetCell == null || strokeCapture == null || imageCapture == null || evaluator == null)
        {
            Debug.LogError("[WritingFeedback] targetCell / strokeCapture / imageCapture / evaluator 참조가 비어 있습니다. 인스펙터를 확인하세요.");
            return;
        }

        isEvaluating = true;
        onStatus?.Invoke("Evaluating...");

        // 1) 하이브리드 캡처 — 획 좌표(획순) + PNG(모양/인식)
        string strokes = strokeCapture.CaptureJson(targetCell);
        byte[] png = imageCapture.CapturePng(targetCell);

        // 2) 요청 구성
        var request = new HandwritingEvaluationRequest
        {
            strokesJson = strokes,
            imagePng = png,
            targetText = targetCell.targetText,
            criteria = criteria
        };

        // 3) 평가 요청 → 콜백으로 결과 수신
        evaluator.Evaluate(request, OnEvaluated);
    }

    void OnEvaluated(HandwritingFeedback feedback)
    {
        isEvaluating = false;

        if (feedback == null)
            feedback = HandwritingFeedback.Error("No evaluation result received.");

        // 인식 글자가 목표와 다르면 통과 불가로 강제 (AI가 관대하게 줘도 코드로 못박음)
        if (targetCell != null && !string.IsNullOrEmpty(targetCell.targetText))
        {
            string target = targetCell.targetText.Trim();
            string recognized = (feedback.recognizedText ?? "").Trim();
            if (recognized != target)
            {
                if (feedback.score > 20) feedback.score = 20;
                feedback.passed = false;
            }
        }

        Debug.Log($"[WritingFeedback] 목표='{targetCell?.targetText}' 인식='{feedback.recognizedText}' 점수={feedback.score} 통과={feedback.passed}");

        onStatus?.Invoke("");
        onFeedback?.Invoke(feedback);
    }
}
