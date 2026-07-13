using UnityEngine;

/// <summary>
/// 스테이지 하나의 데이터.
/// Project 창 우클릭 > Create > WritingGrandfather > Stage Data로 생성.
/// </summary>
[CreateAssetMenu(fileName = "Stage", menuName = "WritingGrandfather/Stage Data")]
public class StageData : ScriptableObject
{
    [Header("스테이지 이름 (표시용)")]
    public string stageName;

    [Header("낱말 모드에서 나올 외자들 (붙여서 입력, 예: 가나무물불)")]
    public string letters = "";

    [Header("단어 모드에서 나올 단어들")]
    public string[] words;

    /// <summary>현재 모드에 맞는 텍스트 목록을 돌려준다.</summary>
    public string[] GetTexts(GameMode mode)
    {
        if (mode == GameMode.Word)
            return words ?? new string[0];

        var result = new string[letters.Length];
        for (int i = 0; i < letters.Length; i++)
            result[i] = letters[i].ToString();
        return result;
    }
}
