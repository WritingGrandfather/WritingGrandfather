using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using TMPro;

/// <summary>
/// [Tools > Build Jihwan Scene] 메뉴로 JihwanScnene을 생성한다.
///
/// 방식: 팀원이 만든 그리기 UI 씬(Hong/Scene/New Scene 1.unity)을 베이스로 열고,
///       그 위에 AI 인식·평가 부분만 얹은 뒤 JihwanScnene으로 저장한다.
///       (팀 원본 씬은 건드리지 않음 — 다른 경로로 저장)
///
/// 팀 씬이 이미 제공: Main Camera, Pool Manager, UndoManager, Drow Line, Canvas(색상/연필/지우개/Undo/슬라이더), EventSystem
/// 여기서 추가:       WritingCell, CellCapture, GrokEvaluator, WritingFeedbackController, [Evaluate] 버튼 + 결과/상태 텍스트
///
/// UI 언어: 영어
/// </summary>
public static class JihwanSceneBuilder
{
    const string ScenePath = "Assets/1_Scenes/JihwanScnene.unity";
    const string TeamScenePath = "Assets/Hong/Scene/New Scene 1.unity";

    static TMP_FontAsset koreanFont; // 인식된 한글(예: 목표 글자) 표시용 폰트

    [MenuItem("Tools/Build Jihwan Scene")]
    public static void Build()
    {
        // 팀 UI 씬을 베이스로 연다
        var scene = EditorSceneManager.OpenScene(TeamScenePath, OpenSceneMode.Single);

        koreanFont = FindKoreanFont(); // 인식 결과에 한글이 섞이므로 폰트는 여전히 필요

        // 팀 씬에 이미 있는 오브젝트 찾기
        var canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("실패", "팀 씬에서 Canvas를 찾지 못했습니다.", "확인");
            return;
        }

        // ── AI 인식/평가 오브젝트 추가 ──────────────────────────────
        var cellGO = new GameObject("WritingCell");
        var cell = cellGO.AddComponent<WritingCell>();
        cell.size = new Vector2(5f, 5f);
        cell.targetText = "가"; // 목표 글자 (인스펙터에서 수정 가능)
        cellGO.transform.position = Vector3.zero;

        var capGO = new GameObject("CellCapture");
        var cap = capGO.AddComponent<CellCapture>();
        cap.resolution = 512;
        cap.backgroundColor = Color.white;

        var evalGO = new GameObject("OpenAIEvaluator");
        var evaluator = evalGO.AddComponent<OpenAIHandwritingEvaluator>();

        // ── UI 추가 (팀 Canvas 위에) ────────────────────────────────
        var feedbackTMP = MakeText("AI_FeedbackText", canvas.transform,
            new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(900, 180), 30,
            "Write in the cell, then press [Evaluate]");

        var statusTMP = MakeText("AI_StatusText", canvas.transform,
            new Vector2(0.5f, 1f), new Vector2(0, -210), new Vector2(600, 50), 28, "");

        MakeButton("AI_EvaluateButton", canvas.transform,
            new Vector2(1f, 1f), new Vector2(-20, -20), new Vector2(240, 90),
            "Evaluate", new Color(0.20f, 0.55f, 0.90f), out Button evaluateButton);

        // 이벤트 → 텍스트 브릿지
        var display = canvas.gameObject.AddComponent<FeedbackDisplayUI>();
        display.feedbackText = feedbackTMP;
        display.statusText = statusTMP;

        // ── 전체 조율 컨트롤러 ──────────────────────────────────────
        var ctrlGO = new GameObject("WritingFeedbackController");
        var ctrl = ctrlGO.AddComponent<WritingFeedbackController>();
        var ctrlSO = new SerializedObject(ctrl);
        ctrlSO.FindProperty("targetCell").objectReferenceValue = cell;
        ctrlSO.FindProperty("capture").objectReferenceValue = cap;
        ctrlSO.FindProperty("evaluator").objectReferenceValue = evaluator;
        ctrlSO.FindProperty("criteria").stringValue =
            "You are a teacher helping a learner practice Korean handwriting. " +
            "Recognize the handwriting in the image and compare it with the target character. " +
            "Judge by: 1) matches the target, 2) stroke clarity, 3) stays inside the cell. " +
            "Give warm, encouraging feedback in one or two sentences, in English.";
        ctrlSO.ApplyModifiedPropertiesWithoutUndo();

        if (ctrl.onFeedback == null) ctrl.onFeedback = new FeedbackEvent();
        if (ctrl.onStatus == null) ctrl.onStatus = new StatusEvent();

        // ── 연결 ────────────────────────────────────────────────────
        UnityEventTools.AddPersistentListener(evaluateButton.onClick, ctrl.RequestFeedback);
        UnityEventTools.AddPersistentListener<HandwritingFeedback>(ctrl.onFeedback, display.OnFeedback);
        UnityEventTools.AddPersistentListener<string>(ctrl.onStatus, display.OnStatus);

        // ── JihwanScnene으로 저장 (팀 원본 씬은 그대로 유지) ────────
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        if (ok)
        {
            Debug.Log($"[JihwanSceneBuilder] 팀 UI 기반 씬 생성 완료: {ScenePath}");
            EditorUtility.DisplayDialog("Done",
                $"JihwanScnene created from the team UI scene.\n{ScenePath}\n\nMake sure ApiKeyConfig has your xAI key, then press Play.",
                "OK");
        }
        else
        {
            Debug.LogError("[JihwanSceneBuilder] 씬 저장 실패");
        }
    }

    // 프로젝트 안의 한글 TMP 폰트 에셋을 이름 힌트로 자동 탐색
    static TMP_FontAsset FindKoreanFont()
    {
        string[] hints = { "KR", "Kor", "Nanum", "Malgun", "Noto", "Gothic", "Pretendard", "Spoqa", "나눔", "고딕", "본고딕" };
        foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var h in hints)
                if (name.IndexOf(h, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }
        return null;
    }

    // ── UI 헬퍼 ────────────────────────────────────────────────────

    // anchor: (0.5,1)=상단중앙, (1,1)=우상단 등. pos: 앵커 기준 오프셋
    static TextMeshProUGUI MakeText(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;
        if (koreanFont != null) tmp.font = koreanFont;
        return tmp;
    }

    static GameObject MakeButton(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, string label, Color color, out Button button)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        go.GetComponent<Image>().color = color;
        button = go.GetComponent<Button>();

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 32;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        if (koreanFont != null) tmp.font = koreanFont;

        return go;
    }
}
