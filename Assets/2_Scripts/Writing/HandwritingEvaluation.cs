using System;
using UnityEngine;

/// <summary>
/// AI에게 보낼 평가 요청. 칸 캡처 이미지 + 목표 글자 + 판정 기준(기획 조건)을 담는다.
/// </summary>
[Serializable]
public class HandwritingEvaluationRequest
{
    public string strokesJson;   // 획 좌표 JSON (StrokeCapture.CaptureJson 결과) — 라인렌더러 기반
    public byte[] imagePng;      // 인식용 PNG (StrokeRasterizer로 그린 이미지)
    public string targetText;    // 이 칸에서 써야 할 목표 글자 (없으면 빈 문자열)
    public string criteria;      // 기획에 정의된 판정 기준 (프롬프트로 그대로 사용)
    public string[] candidates;  // (선택) 후보 글자 목록 — 현재 스테이지의 글자들. 닫힌 집합 인식으로 정확도↑
}

/// <summary>
/// AI가 돌려주는 평가 결과.
/// </summary>
[Serializable]
public class HandwritingFeedback
{
    public string recognizedText; // AI가 이미지에서 읽어낸 글자
    public int score;             // 0~100 종합 점수 (통과 판정에 사용)
    public bool passed;           // 통과 여부
    public int stars;             // 별점 0~3 (불통과=0, 통과=최소1, 70↑=2, 85↑=3)
    public string message;        // 사용자에게 보여줄 피드백 문장

    // 세부 항목 점수 (0~100). 아직 계산 안 됐으면 -1 (UI는 이 경우 종합 score로 대체 표시).
    public int similarityScore = -1;   // 글자 유사도 — AI가 판단한 모양/인식 신뢰도
    public int strokeOrderScore = -1;  // 획 순서 정확도 — StrokeOrderValidator/Checker 결과
    public int positionScore = -1;     // 글자 크기·위치 정확도 — 칸 대비 크기/중앙 정렬/이탈 정도

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
