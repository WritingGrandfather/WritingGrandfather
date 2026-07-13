using System;
using UnityEngine;

// 로비 설정의 "성인/어린이" 선택을 앱 전역에서 읽을 수 있게 하는 정적 프로필.
// LocalizationManager와 같은 방식: 씬에 아무것도 배치하지 않아도 되고, PlayerPrefs가
// 단일 원본이라 어느 씬에서 먼저 접근하든 저장된 값을 그대로 읽는다. 기본값은 성인.
public static class UserProfile
{
    const string AgeModePrefsKey = "user.age_mode";

    public static event Action OnAgeModeChanged;

    public static bool IsChildMode
    {
        get => PlayerPrefs.GetInt(AgeModePrefsKey, 0) == 1;
        set
        {
            if (IsChildMode == value) return;
            PlayerPrefs.SetInt(AgeModePrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            OnAgeModeChanged?.Invoke();
        }
    }
}
