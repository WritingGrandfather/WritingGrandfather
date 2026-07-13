using UnityEngine;

/// <summary>
/// 초성/중성/종성을 유니코드 조합 공식으로 합쳐 완성형 한글 한 글자를 만든다.
/// 완성형 = 0xAC00 + (초성 index × 588) + (중성 index × 28) + 종성 index
/// </summary>
public static class HangulComposer
{
    // 유니코드 표준 순서 (index가 곧 조합 공식의 index)
    public const string Choseong = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";              // 19개
    public const string Jungseong = "ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ";           // 21개
    public const string Jongseong = "ㄱㄲㄳㄴㄵㄶㄷㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅄㅅㅆㅇㅈㅊㅋㅌㅍㅎ"; // 27개 (index 1~27, 0은 받침 없음)

    /// <summary>
    /// 조합. jongseong에 '\0'을 넘기면 받침 없음.
    /// 풀에 없는 자모가 들어오면 '\0' 반환.
    /// </summary>
    public static char Compose(char choseong, char jungseong, char jongseong = '\0')
    {
        int cho = Choseong.IndexOf(choseong);
        int jung = Jungseong.IndexOf(jungseong);
        int jong = jongseong == '\0' ? 0 : Jongseong.IndexOf(jongseong) + 1;

        if (cho < 0 || jung < 0 || jong < 0)
        {
            Debug.LogWarning($"잘못된 자모: {choseong}/{jungseong}/{jongseong}");
            return '\0';
        }

        return (char)(0xAC00 + cho * 588 + jung * 28 + jong);
    }

    /// <summary>완성형 한글을 초/중/종성으로 분해한다. 종성 없으면 jong='\0'.</summary>
    public static bool Decompose(char c, out char cho, out char jung, out char jong)
    {
        cho = jung = jong = '\0';
        if (c < 0xAC00 || c > 0xD7A3) return false;

        int idx = c - 0xAC00;
        cho = Choseong[idx / 588];
        jung = Jungseong[idx % 588 / 28];
        int j = idx % 28;
        if (j > 0) jong = Jongseong[j - 1];
        return true;
    }

    /// <summary>자모 하나의 통상적인 손글씨 획수 (대략치).</summary>
    public static int JamoStrokeCount(char jamo)
    {
        switch (jamo)
        {
            case 'ㄱ': case 'ㄴ': case 'ㅇ': case 'ㅡ': case 'ㅣ': return 1;
            case 'ㄷ': case 'ㅅ': case 'ㅈ': case 'ㅋ': case 'ㄲ':
            case 'ㅏ': case 'ㅓ': case 'ㅗ': case 'ㅜ': case 'ㅢ': return 2;
            case 'ㄹ': case 'ㅁ': case 'ㅊ': case 'ㅌ': case 'ㅎ':
            case 'ㅑ': case 'ㅕ': case 'ㅛ': case 'ㅠ': case 'ㅐ': case 'ㅔ': case 'ㅚ': case 'ㅟ': return 3;
            case 'ㅂ': case 'ㄸ': case 'ㅆ':
            case 'ㅒ': case 'ㅖ': case 'ㅘ': case 'ㅝ': return 4;
            case 'ㅙ': case 'ㅞ': return 5;
            case 'ㅃ': return 8;
            // 겹받침은 구성 자모 합
            case 'ㄳ': return 3;
            case 'ㄵ': return 3;
            case 'ㄶ': return 4;
            case 'ㄺ': return 4;
            case 'ㄻ': return 6;
            case 'ㄼ': return 7;
            case 'ㄽ': return 5;
            case 'ㄾ': return 6;
            case 'ㄿ': return 7;
            case 'ㅀ': return 6;
            case 'ㅄ': return 6;
            default: return 0;
        }
    }

    // 손글씨에서 서로 헷갈리기 쉬운 자모 (모양 기준)
    static string Confusables(char jamo)
    {
        switch (jamo)
        {
            case 'ㄱ': return "ㄴㅋ";
            case 'ㄴ': return "ㄱㄷ";
            case 'ㄷ': return "ㄴㄹㅌ";
            case 'ㄹ': return "ㄷㅌ";
            case 'ㅁ': return "ㅇㅂ";
            case 'ㅂ': return "ㅁㅍ";
            case 'ㅅ': return "ㅈㅊ";
            case 'ㅇ': return "ㅁㅎ";
            case 'ㅈ': return "ㅅㅊ";
            case 'ㅊ': return "ㅈㅅ";
            case 'ㅋ': return "ㄱ";
            case 'ㅌ': return "ㄷㄹ";
            case 'ㅍ': return "ㅂ";
            case 'ㅎ': return "ㅇ";
            case 'ㅏ': return "ㅓㅣㅑ";
            case 'ㅓ': return "ㅏㅕㅣ";
            case 'ㅗ': return "ㅜㅛ";
            case 'ㅜ': return "ㅗㅠ";
            case 'ㅡ': return "ㅜ";
            case 'ㅣ': return "ㅏㅓ";
            case 'ㅐ': return "ㅔㅑ";
            case 'ㅔ': return "ㅐㅕ";
            case 'ㅑ': return "ㅏㅕ";
            case 'ㅕ': return "ㅓㅑ";
            case 'ㅛ': return "ㅗ";
            case 'ㅠ': return "ㅜ";
            default: return "";
        }
    }

    /// <summary>
    /// 목표 외자로부터 "헷갈리기 쉬운 글자들 + 목표"의 후보군을 만든다. (닫힌 집합 인식용)
    /// 초성/중성을 비슷한 자모로 바꾼 글자, 받침을 넣거나 뺀 글자를 섞는다.
    /// 목표가 완성형 한 글자가 아니면 null.
    /// </summary>
    public static string[] GenerateConfusables(string target)
    {
        if (string.IsNullOrEmpty(target) || target.Length != 1) return null;
        if (!Decompose(target[0], out char cho, out char jung, out char jong)) return null;

        var set = new System.Collections.Generic.List<string> { target };
        void Add(char c) { string s = c.ToString(); if (c != '\0' && !set.Contains(s)) set.Add(s); }

        foreach (char c in Confusables(cho)) Add(Compose(c, jung, jong));
        foreach (char j in Confusables(jung)) Add(Compose(cho, j, jong));

        if (jong == '\0')
        {
            Add(Compose(cho, jung, 'ㄱ'));
            Add(Compose(cho, jung, 'ㄴ'));
            Add(Compose(cho, jung, 'ㅇ'));
        }
        else
        {
            Add(Compose(cho, jung));                  // 받침 뺀 것
            foreach (char j in Confusables(jong)) Add(Compose(cho, jung, j));
        }

        // 목표가 항상 첫 번째면 위치로 티가 나므로 셔플
        for (int i = set.Count - 1; i > 0; i--)
        {
            int k = UnityEngine.Random.Range(0, i + 1);
            (set[i], set[k]) = (set[k], set[i]);
        }
        return set.ToArray();
    }

    /// <summary>
    /// 글자(또는 단어)의 자모 구성과 예상 획수를 사람이/AI가 읽을 수 있는 문장으로 만든다.
    /// 예: "'강' = ㄱ(초성) + ㅏ(중성) + ㅇ(종성), 예상 획수 약 4획"
    /// </summary>
    public static string DescribeJamo(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            if (!Decompose(c, out char cho, out char jung, out char jong)) continue;

            int strokes = JamoStrokeCount(cho) + JamoStrokeCount(jung) + (jong != '\0' ? JamoStrokeCount(jong) : 0);
            sb.Append($"'{c}' = {cho}(initial) + {jung}(vowel)");
            if (jong != '\0') sb.Append($" + {jong}(final)");
            sb.Append($", expected ~{strokes} strokes. ");
        }
        return sb.ToString();
    }
}
