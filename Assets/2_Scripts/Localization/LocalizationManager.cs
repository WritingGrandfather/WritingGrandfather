using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum Language
{
    Korean,
    English,
}

// Resources/Localization/localization.csv (형식: key,ko,en)를 읽어 키별로
// 한국어/영어 텍스트를 내려주는 정적 매니저. 씬에 아무것도 배치하지 않아도
// Get()/SetLanguage()를 처음 호출하는 시점에 자동으로 CSV를 로드한다.
public static class LocalizationManager
{
    const string ResourcePath = "Localization/localization";
    const string LanguagePrefsKey = "Localization.Language";

    static Dictionary<string, (string ko, string en)> s_Entries;
    static Language s_CurrentLanguage;

    public static event Action OnLanguageChanged;

    public static Language CurrentLanguage
    {
        get
        {
            EnsureLoaded();
            return s_CurrentLanguage;
        }
    }

    public static void SetLanguage(Language language)
    {
        EnsureLoaded();
        if (s_CurrentLanguage == language)
            return;

        s_CurrentLanguage = language;
        PlayerPrefs.SetInt(LanguagePrefsKey, (int)language);
        PlayerPrefs.Save();
        OnLanguageChanged?.Invoke();
    }

    // key에 대응하는 현재 언어 텍스트를 돌려준다. 키가 없으면 화면에서 바로
    // 눈에 띄도록 "!key!" 형태로 돌려주고 콘솔에 경고를 남긴다.
    public static string Get(string key)
    {
        EnsureLoaded();
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (!s_Entries.TryGetValue(key, out var entry))
        {
            Debug.LogWarning($"[Localization] Missing key: \"{key}\"");
            return $"!{key}!";
        }

        return s_CurrentLanguage == Language.Korean ? entry.ko : entry.en;
    }

    // CSV를 수정한 뒤(에디터 플레이 중 등) 다시 불러오고 싶을 때 사용.
    public static void Reload()
    {
        s_Entries = null;
        EnsureLoaded();
    }

    static void EnsureLoaded()
    {
        if (s_Entries != null)
            return;

        s_CurrentLanguage = (Language)PlayerPrefs.GetInt(LanguagePrefsKey, (int)Language.Korean);
        s_Entries = new Dictionary<string, (string ko, string en)>();

        var csv = Resources.Load<TextAsset>(ResourcePath);
        if (csv == null)
        {
            Debug.LogError($"[Localization] Resources/{ResourcePath}.csv 를 찾을 수 없습니다.");
            return;
        }

        ParseCsv(csv.text, s_Entries);
    }

    static void ParseCsv(string csvText, Dictionary<string, (string ko, string en)> target)
    {
        var rows = SplitCsvRows(csvText);
        for (int i = 1; i < rows.Count; i++) // 0번째 행은 header(key,ko,en)
        {
            var fields = SplitCsvLine(rows[i]);
            if (fields.Count < 3)
                continue;

            var key = fields[0].Trim();
            if (string.IsNullOrEmpty(key))
                continue;

            target[key] = (fields[1], fields[2]);
        }
    }

    // 단순 Split('\n')은 따옴표로 감싼 필드 안의 개행까지 다른 행으로 잘라버리므로,
    // 따옴표 상태를 추적하며 실제 행 경계만 자른다.
    static List<string> SplitCsvRows(string text)
    {
        var rows = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
                inQuotes = !inQuotes;

            if (c == '\n' && !inQuotes)
            {
                rows.Add(current.ToString());
                current.Clear();
            }
            else if (c != '\r')
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            rows.Add(current.ToString());

        return rows;
    }

    // 따옴표로 감싼 필드 안의 콤마/이스케이프된 큰따옴표("")를 처리하는 CSV 필드 분리.
    static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
