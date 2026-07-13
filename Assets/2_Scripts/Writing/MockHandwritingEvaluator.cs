using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// AI 모델을 아직 안 붙인 상태에서 전체 흐름(캡처→평가요청→피드백 표시)을 테스트하기 위한 가짜 평가기.
/// 실제 API 대신 잠깐 대기 후 더미 결과를 돌려준다.
/// 실제 모델이 정해지면 HandwritingEvaluator를 상속한 진짜 구현으로 교체하면 된다.
/// </summary>
public class MockHandwritingEvaluator : HandwritingEvaluator
{
    [Tooltip("응답까지 대기 시간(초) — 실제 네트워크 지연 흉내")]
    public float fakeDelay = 0.5f;

    public override void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        StartCoroutine(FakeEvaluate(request, onComplete));
    }

    IEnumerator FakeEvaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        yield return new WaitForSeconds(fakeDelay);

        int kb = request.imagePng != null ? request.imagePng.Length / 1024 : 0;
        Debug.Log($"[MockEvaluator] 캡처 {kb}KB / 목표='{request.targetText}' 평가 요청 수신");

        // 목표 글자가 있으면 그걸 그대로 인식했다고 가정한 더미 결과
        string recognized = string.IsNullOrEmpty(request.targetText) ? "가" : request.targetText;
        var feedback = new HandwritingFeedback
        {
            recognizedText = recognized,
            score = 85,
            passed = true,
            message = $"'{recognized}' 글자가 잘 써졌어요! (테스트 응답)"
        };

        onComplete?.Invoke(feedback);
    }
}
