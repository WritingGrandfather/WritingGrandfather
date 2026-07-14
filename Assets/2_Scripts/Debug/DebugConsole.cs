using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 화면 위에 최근 로그를 그대로 띄워주는 초간단 인게임 콘솔.
/// 에디터 콘솔을 볼 수 없는 상황(빌드, 기기 테스트, Device Simulator 등)에서
/// Debug.Log/Warning/Error/Exception을 실시간으로 확인하기 위한 용도.
///
/// 토글: 키보드 백틱(`) 키, 또는 화면 좌하단(가로/세로 15% 이내)을 탭.
/// 씬에 하나만 두면 DontDestroyOnLoad로 앱 전체에서 계속 떠 있음(중복 방지 포함).
/// </summary>
public class DebugConsole : MonoBehaviour
{
    static DebugConsole instance;

    [SerializeField] int maxLines = 200;

    bool visible;
    Vector2 scroll;
    readonly List<(string text, LogType type)> lines = new List<(string, LogType)>();
    readonly object lockObj = new object();

    void Awake()
    {
        if (instance != null) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += HandleLog;
    }

    void OnDestroy()
    {
        if (instance == this) Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string message, string stackTrace, LogType type)
    {
        lock (lockObj)
        {
            lines.Add((message, type));
            if (type == LogType.Exception || type == LogType.Error)
                lines.Add(("  " + stackTrace, type));
            while (lines.Count > maxLines) lines.RemoveAt(0);
        }
    }

    void Update()
    {
        // 새 Input System 기준 (레거시 UnityEngine.Input은 이 프로젝트에서 비활성화돼 있어 예외가 남)
        if (Keyboard.current != null && Keyboard.current.backquoteKey.wasPressedThisFrame)
            visible = !visible;

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            Vector2 p = Touchscreen.current.primaryTouch.position.ReadValue();
            if (p.x < Screen.width * 0.15f && p.y < Screen.height * 0.15f)
                visible = !visible;
        }
    }

    void OnGUI()
    {
        if (!visible) return;

        float h = Screen.height * 0.5f;
        GUI.Box(new Rect(0, 0, Screen.width, h), "");
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, h - 40));
        scroll = GUILayout.BeginScrollView(scroll);

        lock (lockObj)
        {
            foreach (var (text, type) in lines)
            {
                GUI.color = type == LogType.Error || type == LogType.Exception ? Color.red
                          : type == LogType.Warning ? Color.yellow
                          : Color.white;
                GUILayout.Label(text);
            }
        }
        GUI.color = Color.white;

        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (GUI.Button(new Rect(Screen.width - 90, h - 34, 80, 28), "Clear"))
            lock (lockObj) lines.Clear();
    }
}
