using System;

/// <summary>
/// 저장할 게임 데이터. 필요에 따라 필드를 자유롭게 추가하면 된다.
/// (로컬 JSON / Firestore 양쪽에서 사용)
/// </summary>
[Serializable]
public class PlayerData
{
    public int clearedStage;     // 클리어한 스테이지 번호
    public int bestScore;        // 최고 점수
    public int totalCharacters;  // 지금까지 쓴 글자 수
    public string lastPlayedIso; // 마지막 플레이 시각(ISO 문자열)
}
