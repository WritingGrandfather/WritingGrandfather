using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OpenAI API로 "획 좌표 데이터"를 인식·평가하는 평가기. (PNG 이미지가 아니라 라인렌더러 기반)
/// 획 순서·획 개수 정보가 담기므로 획순 피드백까지 요청할 수 있다.
///
///  - 엔드포인트: https://api.openai.com/v1/chat/completions
///  - 인증 헤더: Authorization: Bearer <key>
///  - 입력: 텍스트(획 좌표 JSON + 기준)
///
/// API 키(sk-...)는 ApiKeyConfig(Assets/Resources/ApiKeyConfig.asset)에서 가져온다.
/// </summary>
public class OpenAIHandwritingEvaluator : HandwritingEvaluator
{
    // ── AI 모델 칸 ────────────────────────────────────────────────────
    [Header("AI 모델 설정")]
    [Tooltip("사용할 OpenAI 모델 ID. 예: gpt-4o / gpt-4o-mini / gpt-4.1")]
    public string model = "gpt-4o";

    [Tooltip("응답 최대 토큰 수")]
    public int maxTokens = 1024;
    // ────────────────────────────────────────────────────────────────

    const string Endpoint = "https://api.openai.com/v1/chat/completions";

    public override void Evaluate(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        StartCoroutine(SendRequest(request, onComplete));
    }

    IEnumerator SendRequest(HandwritingEvaluationRequest request, Action<HandwritingFeedback> onComplete)
    {
        if (string.IsNullOrEmpty(model))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("No AI model set. Fill the 'model' field on OpenAIHandwritingEvaluator."));
            yield break;
        }

        var config = ApiKeyConfig.Instance;
        if (config == null || string.IsNullOrEmpty(config.ApiKey))
        {
            onComplete?.Invoke(HandwritingFeedback.Error("No API key. Check Assets/Resources/ApiKeyConfig.asset."));
            yield break;
        }

        string prompt = BuildPrompt(request);
        string base64Png = Convert.ToBase64String(request.imagePng ?? Array.Empty<byte>());
        string body = BuildRequestJson(prompt, base64Png);

        using (var www = new UnityWebRequest(Endpoint, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("content-type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(HandwritingFeedback.Error($"API request failed: {www.responseCode} {www.error}\n{www.downloadHandler.text}"));
                yield break;
            }

            string raw = www.downloadHandler.text;
            string aiText = ExtractText(raw);
            if (string.IsNullOrEmpty(aiText))
            {
                Debug.LogError("[OpenAI] content 파싱 실패. raw:\n" + (raw.Length > 2000 ? raw.Substring(0, 2000) : raw));
                onComplete?.Invoke(HandwritingFeedback.Error("Could not read AI content (see Console)."));
                yield break;
            }

            var feedback = ParseFeedback(aiText);
            onComplete?.Invoke(feedback);
        }
    }

    // 기준(criteria) + 목표 글자 + 획 좌표 데이터를 프롬프트로 조합. 결과는 정해진 JSON 형식으로 요청.
    string BuildPrompt(HandwritingEvaluationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(request.criteria);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(request.targetText))
            sb.AppendLine($"Target text: \"{request.targetText}\"");

        sb.AppendLine("You are given the SAME handwriting in two forms:");
        sb.AppendLine("1) An attached IMAGE — use it to recognize the character and judge shape.");
        sb.AppendLine("2) PEN STROKE data — use it to judge stroke order and stroke count.");
        sb.AppendLine("Stroke format: {\"strokes\":[ stroke, ... ]}, each stroke an ordered list of [x,y] points,");
        sb.AppendLine("normalized 0..1 inside the writing cell (x: left→right, y: bottom→top). Array order = drawing order.");
        sb.AppendLine("Strokes:");
        sb.AppendLine(string.IsNullOrEmpty(request.strokesJson) ? "{\"strokes\":[]}" : request.strokesJson);
        sb.AppendLine();
        sb.AppendLine("Evaluate by the criteria above using BOTH the image (shape/recognition) and the strokes (order/count).");
        sb.AppendLine();
        sb.AppendLine("SCORING — be strict, do NOT inflate:");
        sb.AppendLine("1) First decide if the written character actually matches the target.");
        sb.AppendLine("   If it does NOT match (wrong character, illegible, empty, or scribble), score MUST be 0-20 and passed=false.");
        sb.AppendLine("2) Only if it clearly matches the target: 40-59 = poor, 60-79 = okay, 80-100 = good.");
        sb.AppendLine("3) Set passed=true ONLY when it matches the target AND score >= 70.");
        sb.AppendLine("Set recognizedText to what you actually see, even if it differs from the target.");
        sb.AppendLine();
        sb.AppendLine("Do NOT output, echo, describe, or reproduce the image. Do NOT use markdown or code fences.");
        sb.AppendLine("Respond ONLY in this JSON format (no other text):");
        sb.AppendLine("{\"recognizedText\":\"...\",\"score\":0-100 integer,\"passed\":true/false,\"message\":\"feedback sentence\"}");
        return sb.ToString();
    }

    // OpenAI Chat Completions 요청 JSON (텍스트 + 이미지 = 하이브리드)
    string BuildRequestJson(string prompt, string base64Png)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"model\":\"{Escape(model)}\",");
        sb.Append($"\"max_tokens\":{maxTokens},");
        sb.Append("\"messages\":[{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"text\",\"text\":\"");
        sb.Append(Escape(prompt));
        sb.Append("\"}");
        // 이미지가 있으면 함께 첨부
        if (!string.IsNullOrEmpty(base64Png) && base64Png.Length > 4)
        {
            sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,");
            sb.Append(base64Png);
            sb.Append("\"}}");
        }
        sb.Append("]}]}");
        return sb.ToString();
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // 응답에서 choices[0].message.content 문자열 값만 추출.
    // "content" 뒤의 공백/콜론을 건너뛰고, 문자열 이스케이프(\", \n, \uXXXX 등)를 해제한다.
    static string ExtractText(string responseJson)
    {
        int msg = responseJson.IndexOf("\"message\"", StringComparison.Ordinal);
        int from = msg >= 0 ? msg : 0;

        int key = responseJson.IndexOf("\"content\"", from, StringComparison.Ordinal);
        if (key < 0) return "";

        int i = key + "\"content\"".Length;
        // 콜론과 공백 건너뛰기
        while (i < responseJson.Length && (responseJson[i] == ':' || char.IsWhiteSpace(responseJson[i]))) i++;
        // 값이 문자열이 아니면(null 등) 실패
        if (i >= responseJson.Length || responseJson[i] != '"') return "";
        i++; // 여는 따옴표 다음

        var sb = new StringBuilder();
        for (; i < responseJson.Length; i++)
        {
            char c = responseJson[i];
            if (c == '\\' && i + 1 < responseJson.Length)
            {
                char n = responseJson[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case '/': sb.Append('/'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'u':
                        if (i + 4 < responseJson.Length &&
                            int.TryParse(responseJson.Substring(i + 1, 4),
                                System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
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
            return HandwritingFeedback.Error("Empty response from AI.");

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
