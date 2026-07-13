using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

/// <summary>
/// 로그인 화면 컨트롤러 (UI Toolkit).
///
/// 메인 버튼: 구글 로그인 / 이메일로 로그인(팝업) / 게스트로 시작
/// 하단: 회원가입(팝업)
/// 이메일 로그인·회원가입은 각각 모달 팝업에서 처리한다.
/// 성공하면 nextScene 으로 이동. 이전 로그인이 있으면 자동 이동.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class LoginController : MonoBehaviour
{
    [Tooltip("로그인 성공 후 이동할 씬 이름 (Build Settings에 등록되어 있어야 함)")]
    [SerializeField] string nextScene = "UIScene";

    Label _msg;

    // 로그인 팝업
    VisualElement _loginPanel;
    TextField _loginEmail, _loginPassword;
    Label _loginMsg;

    // 회원가입 팝업
    VisualElement _signupPanel;
    TextField _suEmail, _suPassword;
    Label _suMsg;

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _msg = root.Q<Label>("msg");

        _loginPanel = root.Q<VisualElement>("login-panel");
        _loginEmail = root.Q<TextField>("field-login-email");
        _loginPassword = root.Q<TextField>("field-login-password");
        _loginMsg = root.Q<Label>("login-msg");

        _signupPanel = root.Q<VisualElement>("signup-panel");
        _suEmail = root.Q<TextField>("field-signup-email");
        _suPassword = root.Q<TextField>("field-signup-password");
        _suMsg = root.Q<Label>("signup-msg");

        // 메인 버튼
        root.Q<Button>("btn-google").clicked += OnGoogle;
        root.Q<Button>("btn-email-login").clicked += () => Show(_loginPanel, true);
        root.Q<Button>("btn-guest").clicked += OnGuest;
        root.Q<Button>("btn-signup").clicked += () => Show(_signupPanel, true);

        // 로그인 팝업
        root.Q<Button>("btn-login-confirm").clicked += OnLogin;
        root.Q<Button>("btn-login-cancel").clicked += () => Show(_loginPanel, false);

        // 회원가입 팝업
        root.Q<Button>("btn-signup-confirm").clicked += OnSignupConfirm;
        root.Q<Button>("btn-signup-cancel").clicked += () => Show(_signupPanel, false);

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

    // 이메일 로그인 (팝업)
    void OnLogin()
    {
        SetMsg(_loginMsg, "로그인 중...");
        Auth()?.SignInEmail(_loginEmail.value, _loginPassword.value, (ok, m) =>
        {
            if (ok) SceneManager.LoadScene(nextScene);
            else SetMsg(_loginMsg, m);
        });
    }

    // 회원가입 (팝업)
    void OnSignupConfirm()
    {
        SetMsg(_suMsg, "회원가입 중...");
        Auth()?.SignUpEmail(_suEmail.value, _suPassword.value, (ok, m) =>
        {
            if (ok) SceneManager.LoadScene(nextScene); // 가입 성공 = 로그인 상태 → 바로 진행
            else SetMsg(_suMsg, m);
        });
    }

    void OnGoogle()
    {
        SetMsg(_msg, "구글 로그인 중...");
        Auth()?.SignInGoogle((ok, m) =>
        {
            if (ok) SceneManager.LoadScene(nextScene);
            else SetMsg(_msg, m);
        });
    }

    void OnGuest()
    {
        SetMsg(_msg, "시작 중...");
        Auth()?.SignInGuest((ok, m) =>
        {
            if (ok) SceneManager.LoadScene(nextScene);
            else SetMsg(_msg, m);
        });
    }

    void Show(VisualElement panel, bool show)
    {
        if (panel == null) return;
        if (show) panel.RemoveFromClassList("hidden");
        else panel.AddToClassList("hidden");
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
