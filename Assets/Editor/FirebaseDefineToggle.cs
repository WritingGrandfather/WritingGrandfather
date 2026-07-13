using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scripting Define Symbols를 UI에서 찾기 어려울 때, 메뉴로 켜고 끄는 도구.
/// [Tools > Firebase] 에서 현재 선택된 빌드 플랫폼에 심볼을 추가/제거한다.
/// </summary>
public static class FirebaseDefineToggle
{
    const string Firebase = "FIREBASE_ENABLED";
    const string Google = "GOOGLE_SIGNIN";

    [MenuItem("Tools/Firebase/Enable FIREBASE_ENABLED")]
    static void EnableFirebase() => Set(Firebase, true);

    [MenuItem("Tools/Firebase/Disable FIREBASE_ENABLED")]
    static void DisableFirebase() => Set(Firebase, false);

    [MenuItem("Tools/Firebase/Enable GOOGLE_SIGNIN")]
    static void EnableGoogle() => Set(Google, true);

    [MenuItem("Tools/Firebase/Disable GOOGLE_SIGNIN")]
    static void DisableGoogle() => Set(Google, false);

    static void Set(string symbol, bool on)
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        string defs = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);

        var list = new List<string>(defs.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
        bool has = list.Contains(symbol);

        if (on && !has) list.Add(symbol);
        else if (!on && has) list.Remove(symbol);

        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", list));
        Debug.Log($"[Firebase] '{symbol}' {(on ? "추가됨" : "제거됨")} (플랫폼: {group}). 잠시 후 재컴파일됩니다.\n현재 심볼: {string.Join(";", list)}");
    }
}
