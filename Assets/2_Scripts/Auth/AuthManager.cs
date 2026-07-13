using System;
using System.Text.RegularExpressions;
using UnityEngine;
#if FIREBASE_ENABLED
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
#endif

/// <summary>
/// Firebase 기반 인증 매니저. (씬 전환에도 유지되는 싱글톤)
///
/// ★ 컴파일 안전 설계 ★
///  - Firebase SDK를 아직 안 넣었으면 Player Settings의 Scripting Define Symbols에
///    FIREBASE_ENABLED 가 없으므로, Firebase 코드는 컴파일에서 빠진다 → 프로젝트가 안 깨진다.
///  - SDK 임포트 후 FIREBASE_ENABLED 를 추가하면 실제 인증이 동작한다.
///  - 게스트 로그인은 Firebase 없이도 항상 동작한다 (계정 불필요).
///
/// 사용법: UI는 SignInEmail / SignUpEmail / SignInGuest 를 호출하고
///        결과 콜백 Action&lt;bool success, string message&gt; 로 처리한다.
/// </summary>
public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    // 로그인 상태 (다른 스크립트가 참조)
    public bool IsSignedIn { get; private set; }
    public bool IsGuest { get; private set; }
    public string UserId { get; private set; } = "";
    public string DisplayName { get; private set; } = "";

#if FIREBASE_ENABLED
    FirebaseAuth auth;
    bool ready;
#endif

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitFirebase();
    }

    void InitFirebase()
    {
#if FIREBASE_ENABLED
        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                auth = FirebaseAuth.DefaultInstance;
                ready = true;
                Debug.Log("[Auth] Firebase 준비 완료");
            }
            else
            {
                Debug.LogError("[Auth] Firebase 의존성 오류: " + task.Result);
            }
        });
#endif
    }

    // ── 자동 로그인 (앱 재시작 시 이전 로그인 복원) ─────────────────
    const string GuestKey = "auth_guest_login";

    /// <summary>이전에 로그인한 적 있으면 복원한다. onResult(true)면 바로 게임 씬으로 보내면 됨.</summary>
    public void AutoLogin(Action<bool> onResult)
    {
#if FIREBASE_ENABLED
        StartCoroutine(AutoLoginRoutine(onResult));
#else
        if (PlayerPrefs.GetInt(GuestKey, 0) == 1) { SetGuestLocal(); onResult?.Invoke(true); }
        else onResult?.Invoke(false);
#endif
    }

#if FIREBASE_ENABLED
    System.Collections.IEnumerator AutoLoginRoutine(Action<bool> onResult)
    {
        // Firebase 초기화 대기. 콜드 스타트(앱 완전 종료 후 첫 실행)에서는 의존성 점검이
        // 몇 초 이상 걸릴 수 있으므로 넉넉히(최대 20초) 기다린다. 너무 짧게 끊으면
        // 실제로는 로그인돼 있는데도 로그인 화면으로 떨어져 "매번 다시 로그인" 현상이 생긴다.
        float t = 0f;
        while (!ready && t < 20f) { t += Time.deltaTime; yield return null; }

        if (ready && auth.CurrentUser != null)
        {
            ApplyUser(auth.CurrentUser, auth.CurrentUser.IsAnonymous);
            onResult?.Invoke(true);
            yield break;
        }
        if (PlayerPrefs.GetInt(GuestKey, 0) == 1) { SetGuestLocal(); onResult?.Invoke(true); yield break; }
        onResult?.Invoke(false);
    }
#endif

    void SetGuestLocal()
    {
        IsSignedIn = true; IsGuest = true;
        UserId = SystemInfo.deviceUniqueIdentifier; DisplayName = LocalizationManager.Get("auth.guest_display_name");
        PlayerPrefs.SetInt(GuestKey, 1);
    }

    // ── 이메일 회원가입 ─────────────────────────────────────────────
    public void SignUpEmail(string email, string password, Action<bool, string> onDone)
    {
        if (!ValidateInput(email, password, onDone)) return;
#if FIREBASE_ENABLED
        if (!ready) { onDone?.Invoke(false, LocalizationManager.Get("auth.please_wait_init")); return; }
        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(t =>
        {
            if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
            ApplyUser(auth.CurrentUser, guest: false);
            onDone?.Invoke(true, LocalizationManager.Get("auth.signup_complete"));
        });
#else
        onDone?.Invoke(false, LocalizationManager.Get("auth.firebase_not_configured"));
#endif
    }

    // ── 이메일 로그인 ───────────────────────────────────────────────
    public void SignInEmail(string email, string password, Action<bool, string> onDone)
    {
        if (!ValidateInput(email, password, onDone)) return;
#if FIREBASE_ENABLED
        if (!ready) { onDone?.Invoke(false, LocalizationManager.Get("auth.please_wait_init")); return; }
        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(t =>
        {
            if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
            ApplyUser(auth.CurrentUser, guest: false);
            onDone?.Invoke(true, LocalizationManager.Get("auth.login_success"));
        });
#else
        onDone?.Invoke(false, LocalizationManager.Get("auth.firebase_not_configured"));
#endif
    }

    // ── 게스트 로그인 (계정 불필요, 바로 진행) ──────────────────────
    public void SignInGuest(Action<bool, string> onDone)
    {
#if FIREBASE_ENABLED
        if (ready)
        {
            auth.SignInAnonymouslyAsync().ContinueWithOnMainThread(t =>
            {
                if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
                ApplyUser(auth.CurrentUser, guest: true);
                onDone?.Invoke(true, LocalizationManager.Get("auth.guest_start"));
            });
            return;
        }
#endif
        // Firebase 없이도 게스트는 바로 진행 (저장은 로컬로 처리)
        SetGuestLocal();
        onDone?.Invoke(true, LocalizationManager.Get("auth.guest_start"));
    }

    public void SignOut()
    {
#if FIREBASE_ENABLED
        if (auth != null) auth.SignOut();
#endif
        IsSignedIn = false; IsGuest = false; UserId = ""; DisplayName = "";
        PlayerPrefs.DeleteKey(GuestKey);
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────────
    // 이메일 형식: @ 앞뒤에 글자, 뒤엔 점+도메인. 공백/@ 중복 불가.
    // 예) ok: a@b.com   막힘: @asdf.com, a@b, a@@b.com, "a b@c.com"
    static readonly Regex EmailRegex =
        new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    bool ValidateInput(string email, string password, Action<bool, string> onDone)
    {
        email = (email ?? "").Trim();
        if (string.IsNullOrEmpty(email) || !EmailRegex.IsMatch(email))
        { onDone?.Invoke(false, LocalizationManager.Get("auth.invalid_email")); return false; }
        if (string.IsNullOrEmpty(password) || password.Length < 6)
        { onDone?.Invoke(false, LocalizationManager.Get("auth.password_too_short")); return false; }
        return true;
    }

#if FIREBASE_ENABLED
    void ApplyUser(FirebaseUser user, bool guest)
    {
        IsSignedIn = true;
        IsGuest = guest;
        UserId = user != null ? user.UserId : "";
        DisplayName = (user != null && !string.IsNullOrEmpty(user.DisplayName))
            ? user.DisplayName
            : (guest ? LocalizationManager.Get("auth.guest_display_name") : (user != null ? user.Email : ""));
        PlayerPrefs.SetInt(GuestKey, guest ? 1 : 0); // 게스트=로컬저장, 실제계정=클라우드
    }

    static string FirebaseError(System.Threading.Tasks.Task t)
    {
        var e = t.Exception?.GetBaseException();
        return e != null ? LocalizationManager.Get("auth.failure_prefix") + " " + e.Message : LocalizationManager.Get("auth.failure");
    }
#endif
}
