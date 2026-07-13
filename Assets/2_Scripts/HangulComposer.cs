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
}
