using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

/// <summary>
/// 로그인 화면 컨트롤러 (UI Toolkit). Login.uxml의 버튼/입력을 AuthManager에 연결한다.
///
/// 흐름:
///  - 로그인 버튼      → 이메일 로그인
///  - 회원가입 버튼    → 회원가입 패널 열기 → 가입 (일반 로그인과 같은 계정 저장소)
///  - 구글 로그인      → 구글 계정
///  - 게스트로 시작    → 계정 없이 바로 게임 씬으로
///  성공하면 nextScene 으로 이동한다.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LoginController : MonoBehaviour
{
    [Tooltip("로그인 성공 후 이동할 씬 이름 (Build Settings에 등록되어 있어야 함)")]
    [SerializeField] string nextScene = "UIScene";

    TextField _email, _password, _suEmail, _suPassword;
    Label _msg, _suMsg;
    VisualElement _signupPanel;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _email = root.Q<TextField>("field-email");
        _password = root.Q<TextField>("field-password");
        _msg = root.Q<Label>("msg");

        _signupPanel = root.Q<VisualElement>("signup-panel");
        _suEmail = root.Q<TextField>("field-signup-email");
        _suPassword = root.Q<TextField>("field-signup-password");
        _suMsg = root.Q<Label>("signup-msg");

        root.Q<Button>("btn-login").clicked += OnLogin;
        root.Q<Button>("btn-signup").clicked += () => ShowSignup(true);
        root.Q<Button>("btn-google").clicked += OnGoogle;
        root.Q<Button>("btn-guest").clicked += OnGuest;
        root.Q<Button>("btn-signup-confirm").clicked += OnSignupConfirm;
        root.Q<Button>("btn-signup-cancel").clicked += () => ShowSignup(false);

        // 자동 로그인: 이전에 로그인했으면 바로 게임 씬으로
        if (AuthManager.Instance != null)
        {
            SetMsg(_msg, "자동 로그인 확인 중...");
            AuthManager.Instance.AutoLogin(auto =>
            {
                if (auto) SceneManager.LoadScene(nextScene);
                else SetMsg(_msg, "");
            });
        }
    }

    void OnLogin()
    {
        SetMsg(_msg, "로그인 중...");
        Auth().SignInEmail(_email.value, _password.value, Result);
    }

    void OnSignupConfirm()
    {
        SetMsg(_suMsg, "회원가입 중...");
        Auth().SignUpEmail(_suEmail.value, _suPassword.value, (ok, m) =>
        {
            SetMsg(_suMsg, m);
            if (ok) { ShowSignup(false); Result(true, m); } // 가입 성공 = 로그인 상태 → 바로 진행
        });
    }

    void OnGoogle()
    {
        SetMsg(_msg, "구글 로그인 중...");
        Auth().SignInGoogle(Result);
    }

    void OnGuest()
    {
        SetMsg(_msg, "시작 중...");
        Auth().SignInGuest(Result);
    }

    // 로그인/가입 결과 처리
    void Result(bool ok, string message)
    {
        SetMsg(_msg, message);
        if (ok)
            SceneManager.LoadScene(nextScene);
    }

    void ShowSignup(bool show)
    {
        if (_signupPanel == null) return;
        if (show) _signupPanel.RemoveFromClassList("hidden");
        else _signupPanel.AddToClassList("hidden");
    }

    void SetMsg(Label label, string text)
    {
        if (label != null) label.text = text;
    }

    AuthManager Auth()
    {
        if (AuthManager.Instance == null)
        {
            Debug.LogError("[Login] AuthManager가 씬에 없습니다.");
            SetMsg(_msg, "인증 매니저 없음");
        }
        return AuthManager.Instance;
    }
}
