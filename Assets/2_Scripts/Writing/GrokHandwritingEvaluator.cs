using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// xAI Grok Vision API로 칸 이미지를 인식하고 평가하는 평가기.
/// HandwritingEvaluator를 상속하므로, 씬에서 이 컴포넌트를 붙이면 WritingFeedbackController가 그대로 사용한다.
///
/// xAI는 OpenAI 호환 API라서 Claude와 달리:
///  - 엔드포인트: /v1/chat/completions
///  - 인증 헤더: Authorization: Bearer <key>
///  - 이미지: image_url(data URL) 형식
///
/// API 키(xai-...)는 ApiKeyConfig(Assets/Resources/ApiKeyConfig.asset)에서 가져온다.
/// </summary>
public class GrokHandwritingEvaluator : HandwritingEvaluator
{
    // ── AI 모델 칸 ────────────────────────────────────────────────────
    [Header("AI 모델 설정")]
    [Tooltip("사용할 Grok 모델 ID. ")]
    public string model = "grok-4.3";        // 비전 지원 여부는 xAI 공식 문서에서 확인 권장

    [Tooltip("응답 최대 토큰 수")]
    public int maxTokens = 1024;
    // ────────────────────────────────────────────────────────────────

    const string Endpoint = "https://api.x.ai/v1/chat/completions";

    public override void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        StartCoroutine(SendRequest(request, onComplete));
    }

    IEnumerator SendRequest(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        if (string.IsNullOrEmpty(model))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("AI 모델이 설정되지 않았습니다. GrokHandwritingEvaluator의 model 필드를 채우세요."));
            yield break;
        }

        var config = ApiKeyConfig.Instance;
        if (config == null || string.IsNullOrEmpty(config.ApiKey))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("API 키가 없습니다. Assets/Resources/ApiKeyConfig.asset을 확인하세요."));
            yield break;
        }

        string base64Png = Convert.ToBase64String(request.imagePng ?? Array.Empty<byte>());
        string prompt = BuildPrompt(request);
        string body = BuildRequestJson(base64Png, prompt);

        using (var www = new UnityWebRequest(Endpoint, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("content-type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(HandwritingFeedback.Error($"API 요청 실패: {www.responseCode} {www.error}\n{www.downloadHandler.text}"));
                yield break;
            }

            string aiText = ExtractText(www.downloadHandler.text);
            var feedback = ParseFeedback(aiText);
            onComplete?.Invoke(feedback);
        }
    }

    // 기획 기준(criteria) + 목표 글자를 프롬프트로 조합. 결과는 정해진 JSON 형식으로 요청.
    string BuildPrompt(HandwritingEvaluationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.criteria);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(request.targetText))
            sb.AppendLine($"목표 글자: \"{request.targetText}\"");
        sb.AppendLine("첨부된 이미지의 손글씨를 인식하고 위 기준으로 평가하세요.");
        sb.AppendLine("반드시 아래 JSON 형식으로만 답하세요(다른 텍스트 금지):");
        sb.AppendLine("{\"recognizedText\":\"인식한글자\",\"score\":0~100정수,\"passed\":true/false,\"message\":\"피드백문장\"}");
        return sb.ToString();
    }

    // OpenAI 호환 chat/completions 요청 JSON 조립 (텍스트 + 이미지 data URL)
    string BuildRequestJson(string base64Png, string prompt)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"model\":\"{Escape(model)}\",");
        sb.Append($"\"max_tokens\":{maxTokens},");
        sb.Append("\"messages\":[{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"text\",\"text\":\"");
        sb.Append(Escape(prompt));
        sb.Append("\"},");
        sb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,");
        sb.Append(base64Png);
        sb.Append("\"}}");
        sb.Append("]}]}");
        return sb.ToString();
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // 응답에서 choices[0].message.content 값만 추출.
    // "message" 객체 뒤의 첫 "content":"..." 를 읽는다.
    static string ExtractText(string responseJson)
    {
        int msg = responseJson.IndexOf("\"message\"", StringComparison.Ordinal);
        int searchFrom = msg >= 0 ? msg : 0;

        const string key = "\"content\":\"";
        int start = responseJson.IndexOf(key, searchFrom, StringComparison.Ordinal);
        if (start < 0) return "";
        start += key.Length;

        var sb = new StringBuilder();
        for (int i = start; i < responseJson.Length; i++)
        {
            char c = responseJson[i];
            if (c == '\\' && i + 1 < responseJson.Length)
            {
                char n = responseJson[++i];
                sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                continue;
            }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }

    // AI가 돌려준 JSON 텍스트 → HandwritingFeedback
    static HandwritingFeedback ParseFeedback(string aiText)
    {
        if (string.IsNullOrEmpty(aiText))
            return HandwritingFeedback.Error("AI 응답이 비어 있습니다.");

        int open = aiText.IndexOf('{');
        int close = aiText.LastIndexOf('}');
        if (open >= 0 && close > open)
            aiText = aiText.Substring(open, close - open + 1);

        try
        {
            return JsonUtility.FromJson<HandwritingFeedback>(aiText);
        }
        catch
        {
            return new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = aiText };
        }
    }
}
