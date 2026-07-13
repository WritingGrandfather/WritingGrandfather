using System;
using UnityEngine;

/// <summary>
/// AI에게 보낼 평가 요청. 칸 캡처 이미지 + 목표 글자 + 판정 기준(기획 조건)을 담는다.
/// </summary>
[Serializable]
public class HandwritingEvaluationRequest
{
    public byte[] imagePng;   // 칸을 캡처한 PNG (CellCapture.CapturePng 결과)
    public string targetText; // 이 칸에서 써야 할 목표 글자 (없으면 빈 문자열)
    public string criteria;   // 기획에 정의된 판정 기준 (프롬프트로 그대로 사용)
}

/// <summary>
/// AI가 돌려주는 평가 결과.
/// </summary>
[Serializable]
public class HandwritingFeedback
{
    public string recognizedText; // AI가 이미지에서 읽어낸 글자
    public int score;             // 0~100 점수
    public bool passed;           // 통과 여부
    public string message;        // 사용자에게 보여줄 피드백 문장

    public static HandwritingFeedback Error(string reason) =>
        new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = reason };
}

/// <summary>
/// 손글씨 평가기의 공통 베이스.
/// 실제 AI 연동은 이 클래스를 상속해 구현한다. (모델은 나중에 결정 — 지금은 Mock 사용)
/// 인스펙터에서 컴포넌트로 갈아끼울 수 있도록 MonoBehaviour 기반으로 둔다.
/// </summary>
public abstract class HandwritingEvaluator : MonoBehaviour
{
    /// <summary>
    /// 요청을 평가하고 결과를 onComplete 콜백으로 돌려준다.
    /// (네트워크 호출은 비동기이므로 콜백 방식 사용)
    /// </summary>
    public abstract void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete);
}
