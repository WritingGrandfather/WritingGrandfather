using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Claude Vision API로 칸 이미지를 인식하고 평가하는 실제 평가기.
/// HandwritingEvaluator를 상속하므로, 씬에서 MockHandwritingEvaluator 대신 이 컴포넌트를 붙이면
/// WritingFeedbackController가 그대로 사용한다. (AI 교체 지점이 바로 여기)
///
/// API 키는 앞서 만든 ApiKeyConfig(Assets/Resources/ApiKeyConfig.asset)에서 가져온다.
/// </summary>
public class ClaudeHandwritingEvaluator : HandwritingEvaluator
{
    // ── AI 모델 칸 (나중에 결정해서 채워넣기) ──────────────────────────
    [Header("AI 모델 설정")]
    [Tooltip("사용할 Claude 모델 ID. 예: claude-opus-4-8 / claude-sonnet-5")]
    public string model = "claude-sonnet-5";   // 품질·가격 균형. 최고 품질은 claude-opus-4-8

    [Tooltip("응답 최대 토큰 수")]
    public int maxTokens = 1024;
    // ────────────────────────────────────────────────────────────────

    const string Endpoint = "https://api.anthropic.com/v1/messages";
    const string AnthropicVersion = "2023-06-01";

    public override void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        StartCoroutine(SendRequest(request, onComplete));
    }

    IEnumerator SendRequest(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        // 모델 미설정 방지
        if (string.IsNullOrEmpty(model))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("AI 모델이 설정되지 않았습니다. ClaudeHandwritingEvaluator의 model 필드를 채우세요."));
            yield break;
        }

        // API 키 확인
        var config = ApiKeyConfig.Instance;
        if (config == null || string.IsNullOrEmpty(config.ApiKey))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("API 키가 없습니다. Assets/Resources/ApiKeyConfig.asset을 확인하세요."));
            yield break;
        }

        // 이미지 base64 인코딩
        string base64Png = Convert.ToBase64String(request.imagePng ?? Array.Empty<byte>());

        // 요청 본문(JSON) 구성 — 이미지 블록 + 텍스트(기획 기준 + 목표 글자)
        string prompt = BuildPrompt(request);
        string body = BuildRequestJson(base64Png, prompt);

        using (var www = new UnityWebRequest(Endpoint, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("content-type", "application/json");
            www.SetRequestHeader("x-api-key", config.ApiKey);
            www.SetRequestHeader("anthropic-version", AnthropicVersion);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(HandwritingFeedback.Error($"API 요청 실패: {www.responseCode} {www.error}\n{www.downloadHandler.text}"));
                yield break;
            }

            // 응답에서 AI가 돌려준 텍스트를 추출 → 그 텍스트를 HandwritingFeedback JSON으로 파싱
            string aiText = ExtractText(www.downloadHandler.text);
            var feedback = ParseFeedback(aiText);
            onComplete?.Invoke(feedback);
        }
    }

    // 기획 기준(criteria) + 목표 글자를 프롬프트로 조합.
    // AI에게 결과를 정해진 JSON 형식으로 달라고 요청 → 앱에서 파싱하기 쉽게.
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

    // Claude Messages API 요청 JSON 조립 (이미지 + 텍스트 한 메시지)
    string BuildRequestJson(string base64Png, string prompt)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"model\":\"{Escape(model)}\",");
        sb.Append($"\"max_tokens\":{maxTokens},");
        sb.Append("\"messages\":[{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"");
        sb.Append(base64Png);
        sb.Append("\"}},");
        sb.Append("{\"type\":\"text\",\"text\":\"");
        sb.Append(Escape(prompt));
        sb.Append("\"}");
        sb.Append("]}]}");
        return sb.ToString();
    }

    // JSON 문자열 이스케이프 (따옴표/역슬래시/개행)
    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // 응답 JSON에서 content[0].text 값만 추출 (JsonUtility로는 중첩 배열 파싱이 번거로워 최소 파싱)
    static string ExtractText(string responseJson)
    {
        // "text":"..." 첫 번째 값을 찾아 반환
        const string key = "\"text\":\"";
        int start = responseJson.IndexOf(key, StringComparison.Ordinal);
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

        // 혹시 ```json ... ``` 코드블록으로 감싸져 오면 중괄호 구간만 잘라냄
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
            // JSON 파싱 실패 시 원문을 메시지로 그대로 보여줌
            return new HandwritingFeedback { recognizedText = "", score = 0, passed = false, message = aiText };
        }
    }
}
