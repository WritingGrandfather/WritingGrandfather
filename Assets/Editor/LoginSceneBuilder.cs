using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// [Tools > Build Login Scene] 메뉴로 LoginScene을 생성한다.
/// UI Toolkit(UIDocument) + Login.uxml + LobbyPanelSettings + LoginController + AuthManager 구성.
/// </summary>
public static class LoginSceneBuilder
{
    const string ScenePath = "Assets/1_Scenes/LoginScene.unity";
    const string UxmlPath = "Assets/3_UI/UXML/Login.uxml";
    const string PanelPath = "Assets/3_UI/LobbyPanelSettings.asset";

    [MenuItem("Tools/Build Login Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.98f, 0.94f, 0.87f);
        camGO.transform.position = new Vector3(0, 0, -10);

        // 인증/저장 매니저 (씬 전환에도 유지됨)
        new GameObject("AuthManager").AddComponent<AuthManager>();
        new GameObject("SaveManager").AddComponent<SaveManager>();

        // UI Toolkit 문서
        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
        if (uxml == null) Debug.LogWarning($"[LoginSceneBuilder] UXML 없음: {UxmlPath}");
        if (panel == null) Debug.LogWarning($"[LoginSceneBuilder] PanelSettings 없음: {PanelPath}");

        var uiGO = new GameObject("Login UI");
        var doc = uiGO.AddComponent<UIDocument>();
        doc.panelSettings = panel;
        doc.visualTreeAsset = uxml;

        // 세이프에어리어(노치/펀치홀) 자동 회피 — login-root에 패딩
        var safe = uiGO.AddComponent<SafeAreaFitter>();
        var safeSO = new SerializedObject(safe);
        safeSO.FindProperty("targetElementName").stringValue = "login-root";
        safeSO.ApplyModifiedPropertiesWithoutUndo();

        uiGO.AddComponent<LoginController>();

        // UI Toolkit 런타임 입력용 EventSystem
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        EditorSceneManager.MarkSceneDirty(scene);
        bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        if (ok)
        {
            Debug.Log($"[LoginSceneBuilder] 로그인 씬 생성 완료: {ScenePath}");
            EditorUtility.DisplayDialog("Done",
                $"LoginScene 생성 완료!\n{ScenePath}\n\nBuild Settings에 LoginScene과 이동 대상 씬(UIScene)을 등록하세요.",
                "OK");
        }
        else Debug.LogError("[LoginSceneBuilder] 씬 저장 실패");
    }
}
