using System;
using System.Collections;
using System.Collections.Generic;
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
    [Tooltip("사용할 OpenAI 모델 ID.\n추천: gpt-5.4-mini (빠르고 정확, 저렴) / gpt-5.4 (더 정확) / gpt-4o (구형, 다수결 투표 사용)")]
    public string model = "gpt-5.4-mini";

    [Tooltip("응답 최대 토큰 수")]
    public int maxTokens = 1024;

    [Tooltip("다수결 표본 수. 같은 글씨에 대해 N개의 판독을 받아 투표로 결정 (1=투표 없음, 3 권장)\ngpt-5 계열은 병렬 요청으로, 구형 모델은 n 파라미터로 처리 (지연시간 증가 없음)")]
    [Range(1, 5)]
    public int votes = 3;

    [Tooltip("추론 강도 (gpt-5 계열 전용): none / low / medium / high. 높을수록 정확·느림. 비우면 모델 기본값(medium)")]
    public string reasoningEffort = "low";

    /// <summary>gpt-5 계열/o 계열 = 추론 모델 (파라미터 체계가 다름)</summary>
    bool IsReasoningFamily =>
        !string.IsNullOrEmpty(model) &&
        (model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase) ||
         model.StartsWith("o", StringComparison.OrdinalIgnoreCase));
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

        // 추론 모델은 n 파라미터가 없으므로 같은 요청을 votes개 병렬로 보내 다수결.
        // (비추론 모델은 요청 1개 안에서 n으로 처리되므로 병렬 1개)
        int parallel = IsReasoningFamily ? Mathf.Clamp(votes, 1, 5) : 1;

        var aiTexts = new List<string>();
        string lastError = null;
        int done = 0;

        for (int i = 0; i < parallel; i++)
        {
            StartCoroutine(SendOne(body, config.ApiKey, (texts, error) =>
            {
                done++;
                if (error != null) lastError = error;
                else aiTexts.AddRange(texts);
            }));
        }

        while (done < parallel) yield return null;

        if (aiTexts.Count == 0)
        {
            onComplete?.Invoke(HandwritingFeedback.Error(lastError ?? "Could not read AI content (see Console)."));
            yield break;
        }

        // 디버그: AI의 분석 과정을 그대로 출력 (오판 원인 추적용)
        Debug.Log("[OpenAI] AI 분석 원문:\n" + aiTexts[0]);

        // 표본들을 파싱해서 다수결
        var samples = new List<HandwritingFeedback>();
        foreach (string t in aiTexts)
            samples.Add(ParseFeedback(t));

        var feedback = Vote(samples);
        onComplete?.Invoke(feedback);
    }

    // 요청 1개 전송. 성공 시 choices의 content들, 실패 시 error 문자열을 콜백으로 전달.
    IEnumerator SendOne(string body, string apiKey, Action<List<string>, string> callback)
    {
        using (var www = new UnityWebRequest(Endpoint, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("content-type", "application/json");
            www.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[OpenAI] API 요청 실패: {www.responseCode} {www.error}\n{www.downloadHandler.text}");
                callback(null, $"API request failed: {www.responseCode} {www.error}");
                yield break;
            }

            string raw = www.downloadHandler.text;
            List<string> texts = ExtractTexts(raw);
            if (texts.Count == 0)
            {
                string hint = raw.Contains("\"finish_reason\":\"length\"") || raw.Contains("max_completion_tokens")
                    ? "응답이 토큰 한도로 잘렸습니다. Max Tokens를 늘려주세요."
                    : "content 파싱 실패.";
                Debug.LogError($"[OpenAI] {hint} raw:\n" + (raw.Length > 2000 ? raw.Substring(0, 2000) : raw));
                callback(null, "Could not read AI content (see Console).");
                yield break;
            }

            callback(texts, null);
        }
    }

    // 기준(criteria) + 목표 글자 + 획 좌표 데이터를 프롬프트로 조합. 결과는 정해진 JSON 형식으로 요청.
    string BuildPrompt(HandwritingEvaluationRequest request)
    {
        // ★ 중요: 목표 글자를 프롬프트에 절대 넣지 않는다.
        //   목표를 알려주면 AI가 그 글자로 보려는 선입견이 생겨 오답도 목표로 인식해버린다.
        //   여기서는 "인식"만 시키고, 목표와의 비교/합격 판정은 코드(WritingFeedbackController)에서 한다.
        var sb = new StringBuilder();
        sb.AppendLine(request.criteria);
        sb.AppendLine();
        sb.AppendLine("Task: identify what Hangul character (or word) is handwritten, with NO assumption about what it should be.");
        sb.AppendLine();

        // ※ 후보군/목표 글자는 절대 프롬프트에 넣지 않는다.
        //   어떤 형태든 "이 중 하나일 것"이라는 힌트는 오답을 그 글자로 끼워맞추는 오판(false positive)을 만든다.
        //   AI는 순수하게 보이는 대로만 읽고, 정답 비교는 코드가 한다.
        sb.AppendLine("You are given the SAME handwriting in two forms:");
        sb.AppendLine("1) An attached IMAGE — black ink on white, cropped to the ink and centered.");
        sb.AppendLine("   It contains ONLY what the pen drew: one Hangul syllable block, or a single jamo, or a scribble.");
        sb.AppendLine("2) PEN STROKE data (most reliable — trust this over the image when they disagree):");
        sb.AppendLine("   {\"strokeCount\":N,\"strokes\":[{\"start\":[x,y],\"end\":[x,y],\"path\":\"...\",\"keyPoints\":[...]},...]}");
        sb.AppendLine("   - Coordinates normalized to the ink bounding box (longer side = 0..1), aspect ratio preserved.");
        sb.AppendLine("     x: left→right, y: top→bottom (SAME as the image).");
        sb.AppendLine("   - Array order = the order the strokes were drawn. start/end = pen down/up.");
        sb.AppendLine("   - \"keyPoints\" = start, corner(s) where the pen turned, end.");
        sb.AppendLine("   - \"path\" = precomputed pen direction sequence. Examples:");
        sb.AppendLine("     \"right,down\" = goes right then turns down = ㄱ shape.");
        sb.AppendLine("     \"down,right\" = goes down then turns right = ㄴ shape.");
        sb.AppendLine("     \"down\" = a plain vertical line. \"right\" = a plain horizontal line.");
        sb.AppendLine("Strokes:");
        sb.AppendLine(string.IsNullOrEmpty(request.strokesJson) ? "{\"strokeCount\":0,\"strokes\":[]}" : request.strokesJson);
        sb.AppendLine();
        sb.AppendLine("HOW TO RECOGNIZE — follow strictly, reason from the stroke data first:");
        sb.AppendLine("Step 1. For each stroke, read its path/start/end and say what line it is (in the analysis).");
        sb.AppendLine("Step 2. Group strokes into jamo. HARD RULES for grouping:");
        sb.AppendLine("        - Writing order is ALWAYS: initial consonant → vowel → final consonant (if any).");
        sb.AppendLine("        - So the FIRST stroke(s) belong to the initial consonant, NEVER to the final.");
        sb.AppendLine("        - A final consonant exists ONLY if extra strokes come AFTER the vowel strokes");
        sb.AppendLine("          AND they sit clearly BELOW both the initial and the vowel.");
        sb.AppendLine("        - If total strokeCount is small (2-3), a final consonant is unlikely — do not invent one.");
        sb.AppendLine("Step 3. Identify each jamo from its strokes:");
        sb.AppendLine("        - ㄱ = ONE stroke \"right,down\". ㄴ = ONE stroke \"down,right\". Never confuse these two.");
        sb.AppendLine("        - Vertical vowel: long \"down\" stroke + short \"right\" branch stroke.");
        sb.AppendLine("          Branch on the RIGHT side of the vertical (branch x > vertical x) = ㅏ.");
        sb.AppendLine("          Branch on the LEFT side (branch x < vertical x) = ㅓ. Compare the numbers explicitly.");
        sb.AppendLine("          No branch = ㅣ. Two branches same side: right = ㅑ, left = ㅕ.");
        sb.AppendLine("        - Horizontal vowel: long \"right\" stroke + short \"down\" stem. Stem above bar = ㅗ, below = ㅜ.");
        sb.AppendLine("        - ㄷ/ㄹ/ㅁ/ㅂ: use strokeCount (ㄷ≈2, ㄹ≈3, ㅁ≈3-4, ㅂ≈4) plus shape.");
        sb.AppendLine("Step 4. Compose the grouped jamo into the syllable = recognizedText.");
        sb.AppendLine("        In the analysis, state the composition explicitly (e.g., initial ㄴ + vowel ㅏ, no final → 나).");
        sb.AppendLine("Step 5. Score 0-100 the legibility of the writing FOR THE CHARACTER YOU RECOGNIZED:");
        sb.AppendLine("        70-100 = the jamo structure is clearly there (shaky, tilted, or uneven strokes are STILL 70+;");
        sb.AppendLine("                 writers are elderly learners — judge structure, never neatness).");
        sb.AppendLine("        50-69  = barely legible, structure partially broken.");
        sb.AppendLine("        below 50 = broken or ambiguous structure.");
        sb.AppendLine("        If illegible, empty, or a scribble: recognizedText=\"\" and score 0-10.");
        sb.AppendLine();
        sb.AppendLine("OUTPUT FORMAT:");
        sb.AppendLine("First: AT MOST 2 short analysis lines (stroke grouping + composition). No braces, no markdown.");
        sb.AppendLine("Then on the LAST line output exactly this JSON (message = one friendly Korean sentence about the writing):");
        sb.AppendLine("{\"recognizedText\":\"...\",\"score\":0-100 integer,\"message\":\"...\"}");
        return sb.ToString();
    }

    // OpenAI Chat Completions 요청 JSON (텍스트 + 이미지 = 하이브리드)
    string BuildRequestJson(string prompt, string base64Png)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"model\":\"{Escape(model)}\",");

        if (IsReasoningFamily)
        {
            // gpt-5/o 계열: temperature·n 미지원, max_tokens 대신 max_completion_tokens 사용
            // ★ 추론 토큰이 이 예산을 먼저 소모하므로 넉넉해야 답변이 잘리지 않는다 (최소 4096 강제)
            int budget = Mathf.Max(maxTokens, 4096);
            sb.Append($"\"max_completion_tokens\":{budget},");
            if (!string.IsNullOrEmpty(reasoningEffort))
                sb.Append($"\"reasoning_effort\":\"{Escape(reasoningEffort.Trim().ToLowerInvariant())}\",");
        }
        else
        {
            int n = Mathf.Clamp(votes, 1, 5);
            sb.Append($"\"max_tokens\":{maxTokens},");
            // 투표 모드면 표본이 서로 달라지도록 약간의 온도, 단일 모드면 0 (결정적)
            sb.Append(n > 1 ? "\"temperature\":0.5," : "\"temperature\":0,");
            sb.Append($"\"n\":{n},"); // 한 요청으로 N개의 판독 수신 (입력 토큰은 1회만 과금)
        }
        sb.Append("\"messages\":[{\"role\":\"user\",\"content\":[");
        sb.Append("{\"type\":\"text\",\"text\":\"");
        sb.Append(Escape(prompt));
        sb.Append("\"}");
        // 이미지가 있으면 함께 첨부 (detail:high — 저해상도 축소 없이 정밀 분석)
        if (!string.IsNullOrEmpty(base64Png) && base64Png.Length > 4)
        {
            sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,");
            sb.Append(base64Png);
            sb.Append("\",\"detail\":\"high\"}}");
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

    // 응답의 모든 choices[i].message.content 문자열을 추출한다. (n>1 다수결용)
    static List<string> ExtractTexts(string responseJson)
    {
        var results = new List<string>();
        int from = 0;
        while (true)
        {
            int msg = responseJson.IndexOf("\"message\"", from, StringComparison.Ordinal);
            if (msg < 0) break;

            string content = ExtractContentAfter(responseJson, msg, out int next);
            if (!string.IsNullOrEmpty(content)) results.Add(content);
            from = next;
        }
        return results;
    }

    // "message" 위치 뒤에 나오는 "content" 문자열 값을 해제해서 반환한다.
    static string ExtractContentAfter(string responseJson, int from, out int next)
    {
        next = from + "\"message\"".Length;

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

    // 표본들의 recognizedText 다수결. 이긴 그룹의 평균 점수를 사용한다.
    static HandwritingFeedback Vote(List<HandwritingFeedback> samples)
    {
        if (samples.Count == 1) return samples[0];

        var groups = new Dictionary<string, List<HandwritingFeedback>>();
        foreach (var s in samples)
        {
            string key = (s.recognizedText ?? "").Trim();
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<HandwritingFeedback>();
            list.Add(s);
        }

        List<HandwritingFeedback> best = null;
        foreach (var g in groups.Values)
            if (best == null || g.Count > best.Count) best = g;

        int sum = 0;
        foreach (var s in best) sum += s.score;

        HandwritingFeedback result = best[0];
        result.score = Mathf.RoundToInt((float)sum / best.Count);

        Debug.Log($"[OpenAI] 다수결: {best.Count}/{samples.Count}표 → '{result.recognizedText}' (평균 {result.score}점)");
        return result;
    }

    // AI가 돌려준 JSON 텍스트 → HandwritingFeedback
    static HandwritingFeedback ParseFeedback(string aiText)
    {
        if (string.IsNullOrEmpty(aiText))
            return HandwritingFeedback.Error("Empty response from AI.");

        // 분석 문장 뒤 마지막 줄의 JSON만 추출 (결과 JSON은 중첩이 없으므로 마지막 '{'가 시작점)
        int open = aiText.LastIndexOf('{');
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
