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
    [SerializeField] string nextScene = "LobbyScene";

    Label _msg;

    // 로그인 팝업
    VisualElement _loginPanel;
    TextField _loginEmail, _loginPassword;
    Label _loginMsg;

    // 회원가입 팝업
    VisualElement _signupPanel;
    TextField _suEmail, _suPassword;
    Label _suMsg;

    // 정적 UXML 텍스트(로컬라이제이션 대상) - 실행 중 SetMsg()로 갱신되는
    // 상태/에러 메시지는 이번 적용 범위에서 제외했다.
    TextElement _titleLabel, _subtitleLabel;
    Button _btnGoogle, _btnEmailLogin, _btnGuest, _btnSignup;
    TextElement _hintLabel;
    Button _btnLoginConfirm, _btnLoginCancel, _btnSignupConfirm, _btnSignupCancel;
    TextElement _loginModalTitle, _signupModalTitle;

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
        _titleLabel = root.Q<TextElement>("title-label");
        _subtitleLabel = root.Q<TextElement>("subtitle");
        _btnGoogle = root.Q<Button>("btn-google");
        _btnEmailLogin = root.Q<Button>("btn-email-login");
        _btnGuest = root.Q<Button>("btn-guest");
        _btnSignup = root.Q<Button>("btn-signup");
        _hintLabel = root.Q<VisualElement>("footer").Q<TextElement>(className: "hint");
        _loginModalTitle = root.Q<VisualElement>("login-box").Q<TextElement>(className: "modal-text");
        _signupModalTitle = root.Q<VisualElement>("signup-box").Q<TextElement>(className: "modal-text");
        _btnLoginConfirm = root.Q<Button>("btn-login-confirm");
        _btnLoginCancel = root.Q<Button>("btn-login-cancel");
        _btnSignupConfirm = root.Q<Button>("btn-signup-confirm");
        _btnSignupCancel = root.Q<Button>("btn-signup-cancel");

        _btnGoogle.clicked += OnGoogle;

        _btnEmailLogin.clicked += () => Show(_loginPanel, true);
        _btnGuest.clicked += OnGuest;
        _btnSignup.clicked += () => Show(_signupPanel, true);

        // 로그인 팝업
        _btnLoginConfirm.clicked += OnLogin;
        _btnLoginCancel.clicked += () => Show(_loginPanel, false);

        // 회원가입 팝업
        _btnSignupConfirm.clicked += OnSignupConfirm;
        _btnSignupCancel.clicked += () => Show(_signupPanel, false);

        ApplyLocalization();
        LocalizationManager.OnLanguageChanged += ApplyLocalization;

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

    void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= ApplyLocalization;
    }

    void ApplyLocalization()
    {
        _titleLabel.text = LocalizationManager.Get("login.title");
        _subtitleLabel.text = LocalizationManager.Get("login.subtitle");
        _btnGoogle.text = LocalizationManager.Get("login.btn_google");
        _btnEmailLogin.text = LocalizationManager.Get("login.btn_email");
        _btnGuest.text = LocalizationManager.Get("login.btn_guest");
        _hintLabel.text = LocalizationManager.Get("login.hint");
        _btnSignup.text = LocalizationManager.Get("login.btn_signup");

        _loginModalTitle.text = LocalizationManager.Get("login.modal.email_title");
        _loginEmail.label = LocalizationManager.Get("login.field.email_label");
        _loginPassword.label = LocalizationManager.Get("login.field.password_label");
        _btnLoginConfirm.text = LocalizationManager.Get("login.btn.login_confirm");
        _btnLoginCancel.text = LocalizationManager.Get("login.btn.cancel");

        _signupModalTitle.text = LocalizationManager.Get("login.modal.signup_title");
        _suEmail.label = LocalizationManager.Get("login.field.email_label");
        _suPassword.label = LocalizationManager.Get("login.field.password_label");
        _btnSignupConfirm.text = LocalizationManager.Get("login.btn.signup_confirm");
        _btnSignupCancel.text = LocalizationManager.Get("login.btn.cancel");
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
         Debug.LogError("OnGuest Call");
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
