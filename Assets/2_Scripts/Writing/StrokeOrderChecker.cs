using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OpenAI API로 획순·획 방향만 검사하는 검사기.
/// (글자 인식/모양 채점은 TemplateSimilarityEvaluator가 로컬로 처리 — 여기는 순서만 본다)
///
/// 획 데이터에는 그린 순서(배열 순서)와 진행 방향(path)이 들어 있어
/// "ㅏ보다 ㄱ을 먼저 썼는가", "가로획을 왼→오른쪽으로 그었는가" 등을 판정할 수 있다.
/// API 키는 ApiKeyConfig(Assets/Resources/ApiKeyConfig.asset) 사용.
/// </summary>
public class StrokeOrderChecker : MonoBehaviour
{
    [Header("AI 모델 설정")]
    [Tooltip("사용할 모델. 획순 판정은 가볍게 gpt-5.4-mini 권장")]
    public string model = "gpt-5.4-mini";

    [Tooltip("추론 강도 (gpt-5 계열 전용): none/low/medium/high")]
    public string reasoningEffort = "low";

    [Tooltip("응답 최대 토큰 (gpt-5 계열은 최소 4096으로 자동 보정)")]
    public int maxTokens = 4096;

    const string Endpoint = "https://api.openai.com/v1/chat/completions";

    bool IsReasoningFamily =>
        !string.IsNullOrEmpty(model) &&
        (model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
         model.StartsWith("o", StringComparison.OrdinalIgnoreCase));

    [Serializable]
    class OrderResult
    {
        public bool orderCorrect;
        public string message;
    }

    /// <summary>
    /// 획순 검사. onDone(순서정상여부, 피드백문장).
    /// 키가 없거나 요청이 실패하면 게임이 막히지 않도록 '정상'으로 처리한다(fail-open).
    /// </summary>
    public void Check(string targetChar, string strokesJson, Action<bool, string> onDone)
    {
        StartCoroutine(Run(targetChar, strokesJson, onDone));
    }

    IEnumerator Run(string targetChar, string strokesJson, Action<bool, string> onDone)
    {
        var config = ApiKeyConfig.Instance;
        if (config == null || string.IsNullOrEmpty(config.ApiKey) || string.IsNullOrEmpty(model))
        {
            Debug.LogWarning("[StrokeOrder] API 키/모델이 없어 획순 검사를 건너뜁니다.");
            onDone?.Invoke(true, null);
            yield break;
        }

        string body = BuildRequestJson(BuildPrompt(targetChar, strokesJson));

        using (var www = new UnityWebRequest(Endpoint, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("content-type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[StrokeOrder] API 요청 실패: {www.responseCode} {www.error}");
                onDone?.Invoke(true, null); // 네트워크 실패로 게임을 막지 않는다
                yield break;
            }

            string content = ExtractContent(www.downloadHandler.text);
            OrderResult result = Parse(content);
            if (result == null)
            {
                Debug.LogError("[StrokeOrder] 응답 파싱 실패:\n" + content);
                onDone?.Invoke(true, null);
                yield break;
            }

            Debug.Log($"[StrokeOrder] 획순 {(result.orderCorrect ? "정상" : "오류")}: {result.message}");
            onDone?.Invoke(result.orderCorrect, result.message);
        }
    }

    string BuildPrompt(string targetChar, string strokesJson)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Task: check ONLY the stroke ORDER and stroke DIRECTION of the Hangul character \"{targetChar}\" written by hand.");
        sb.AppendLine("Do NOT judge shape quality — shape was already graded elsewhere.");
        sb.AppendLine();

        string jamoInfo = HangulComposer.DescribeJamo(targetChar);
        if (!string.IsNullOrEmpty(jamoInfo))
        {
            sb.AppendLine("Target composition: " + jamoInfo);
            sb.AppendLine();
        }

        sb.AppendLine("Stroke data (array order = the order strokes were drawn; \"path\" = pen direction sequence;");
        sb.AppendLine("coordinates 0..1, x: left→right, y: top→bottom; start/end = pen down/up):");
        sb.AppendLine(strokesJson);
        sb.AppendLine();
        sb.AppendLine("Judge ONLY these two CLEAR violations (flag orderCorrect=false only for these):");
        sb.AppendLine("V1. The VERY FIRST stroke drawn is clearly NOT part of the initial consonant:");
        sb.AppendLine("    - it is the vowel's LONG stroke (the long vertical on the right side, or the long");
        sb.AppendLine("      horizontal bar of ㅗ/ㅜ/ㅡ), OR");
        sb.AppendLine("    - it is clearly a final-consonant stroke at the very bottom.");
        sb.AppendLine("    Check ONLY stroke #1. The order of all later strokes does NOT matter for V1.");
        sb.AppendLine("V2. Reversed direction on a LONG stroke: a long horizontal drawn right→left (path \"left\"),");
        sb.AppendLine("    or a long vertical drawn bottom→top (path \"up\"). Short strokes don't count.");
        sb.AppendLine();
        sb.AppendLine("Do NOT flag (these are acceptable — very important):");
        sb.AppendLine("- ANY ordering among strokes after the first one. Do not try to verify jamo completion order;");
        sb.AppendLine("  grouping strokes to jamo is unreliable, so it is NOT part of this check.");
        sb.AppendLine("- Short strokes/dots/branches anywhere in the sequence, curved or shaky strokes, diagonal paths.");
        sb.AppendLine("- Stroke counts differing from the typical count (shape was already graded elsewhere).");
        sb.AppendLine("- Anything you are not sure about. When uncertain, orderCorrect MUST be true.");
        sb.AppendLine();
        sb.AppendLine("OUTPUT: first 1-2 short analysis lines: what stroke #1 is, and any long reversed stroke (no braces, no markdown).");
        sb.AppendLine("Then on the LAST line exactly this JSON (message = one warm Korean sentence;");
        sb.AppendLine("if wrong, briefly say WHAT to fix, e.g. \"ㄱ을 먼저 쓰고 ㅏ를 써볼까요?\"):");
        sb.AppendLine("{\"orderCorrect\":true/false,\"message\":\"...\"}");
        return sb.ToString();
    }

    string BuildRequestJson(string prompt)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"model\":\"{Escape(model)}\",");
        if (IsReasoningFamily)
        {
            sb.Append($"\"max_completion_tokens\":{Mathf.Max(maxTokens, 4096)},");
            if (!string.IsNullOrEmpty(reasoningEffort))
                sb.Append($"\"reasoning_effort\":\"{Escape(reasoningEffort.Trim().ToLowerInvariant())}\",");
        }
        else
        {
            sb.Append($"\"max_tokens\":{maxTokens},");
            sb.Append("\"temperature\":0,");
        }
        sb.Append("\"messages\":[{\"role\":\"user\",\"content\":\"");
        sb.Append(Escape(prompt));
        sb.Append("\"}]}");
        return sb.ToString();
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    // choices[0].message.content 문자열 추출 (이스케이프 해제 포함)
    static string ExtractContent(string responseJson)
    {
        int msg = responseJson.IndexOf("\"message\"", StringComparison.Ordinal);
        int key = responseJson.IndexOf("\"content\"", msg >= 0 ? msg : 0, StringComparison.Ordinal);
        if (key < 0) return "";

        int i = key + "\"content\"".Length;
        while (i < responseJson.Length && (responseJson[i] == ':' || char.IsWhiteSpace(responseJson[i]))) i++;
        if (i >= responseJson.Length || responseJson[i] != '"') return "";
        i++;

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

    static OrderResult Parse(string content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        int open = content.LastIndexOf('{');
        int close = content.LastIndexOf('}');
        if (open < 0 || close <= open) return null;
        try
        {
            return JsonUtility.FromJson<OrderResult>(content.Substring(open, close - open + 1));
        }
        catch
        {
            return null;
        }
    }
}
