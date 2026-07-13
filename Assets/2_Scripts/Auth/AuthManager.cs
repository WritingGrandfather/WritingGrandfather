using System;
using System.Text.RegularExpressions;
using UnityEngine;
#if FIREBASE_ENABLED
using Firebase;
using Firebase.Auth;
using Firebase.Extensions;
#endif
#if FIREBASE_ENABLED && GOOGLE_SIGNIN
using Google;
#endif

/// <summary>
/// Firebase 기반 인증 매니저. (씬 전환에도 유지되는 싱글톤)
///
/// ★ 컴파일 안전 설계 ★
///  - Firebase SDK를 아직 안 넣었으면 Player Settings의 Scripting Define Symbols에
///    FIREBASE_ENABLED 가 없으므로, Firebase 코드는 컴파일에서 빠진다 → 프로젝트가 안 깨진다.
///  - SDK 임포트 후 FIREBASE_ENABLED 를 추가하면 실제 인증이 동작한다.
///  - 구글 로그인은 추가로 Google Sign-In 플러그인 + GOOGLE_SIGNIN 심볼이 필요하다.
///  - 게스트 로그인은 Firebase 없이도 항상 동작한다 (계정 불필요).
///
/// 사용법: UI는 SignInEmail / SignUpEmail / SignInGoogle / SignInGuest 를 호출하고
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

    [Header("구글 로그인")]
    [Tooltip("Firebase 콘솔 > 프로젝트 설정 > 웹 클라이언트 ID (구글 로그인 켤 때만 사용)")]
#pragma warning disable 0414 // GOOGLE_SIGNIN 꺼져 있을 땐 미사용 — 경고 무시
    [SerializeField] string googleWebClientId = "764618953349-9auq48l3o95177kg9ej1bc692vp1sijd.apps.googleusercontent.com";
#pragma warning restore 0414

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
        float t = 0f;
        while (!ready && t < 5f) { t += Time.deltaTime; yield return null; } // Firebase 초기화 대기

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
        UserId = SystemInfo.deviceUniqueIdentifier; DisplayName = "게스트";
        PlayerPrefs.SetInt(GuestKey, 1);
    }

    // ── 이메일 회원가입 ─────────────────────────────────────────────
    public void SignUpEmail(string email, string password, Action<bool, string> onDone)
    {
        if (!ValidateInput(email, password, onDone)) return;
#if FIREBASE_ENABLED
        if (!ready) { onDone?.Invoke(false, "잠시 후 다시 시도해 주세요. (초기화 중)"); return; }
        auth.CreateUserWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(t =>
        {
            if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
            ApplyUser(auth.CurrentUser, guest: false);
            onDone?.Invoke(true, "회원가입 완료");
        });
#else
        onDone?.Invoke(false, "Firebase 미설정 (FIREBASE_ENABLED 심볼 필요)");
#endif
    }

    // ── 이메일 로그인 ───────────────────────────────────────────────
    public void SignInEmail(string email, string password, Action<bool, string> onDone)
    {
        if (!ValidateInput(email, password, onDone)) return;
#if FIREBASE_ENABLED
        if (!ready) { onDone?.Invoke(false, "잠시 후 다시 시도해 주세요. (초기화 중)"); return; }
        auth.SignInWithEmailAndPasswordAsync(email, password).ContinueWithOnMainThread(t =>
        {
            if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
            ApplyUser(auth.CurrentUser, guest: false);
            onDone?.Invoke(true, "로그인 성공");
        });
#else
        onDone?.Invoke(false, "Firebase 미설정 (FIREBASE_ENABLED 심볼 필요)");
#endif
    }

    // ── 구글 로그인 ─────────────────────────────────────────────────
    public void SignInGoogle(Action<bool, string> onDone)
    {
#if FIREBASE_ENABLED && GOOGLE_SIGNIN
        // 구글 로그인은 안드로이드 네이티브 — 에디터에서는 동작 불가 (currentActivity 없음)
        if (Application.platform != RuntimePlatform.Android)
        {
            onDone?.Invoke(false, "구글 로그인은 안드로이드 기기/빌드에서만 됩니다. 에디터에서는 이메일/게스트로 테스트하세요.");
            return;
        }
        if (!ready) { onDone?.Invoke(false, "잠시 후 다시 시도해 주세요. (초기화 중)"); return; }
        // Configuration은 인스턴스 생성 전 딱 한 번만 설정 가능 — 두 번째부터 설정하면
        // "DefaultInstance already created. Cannot change configuration after creation." 에러가 남
        if (GoogleSignIn.Configuration == null)
        {
            GoogleSignIn.Configuration = new GoogleSignInConfiguration
            {
                WebClientId = googleWebClientId,
                RequestIdToken = true,
                UseGameSignIn = false
            };
        }
        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled) { onDone?.Invoke(false, "구글 로그인 취소됨"); return; }
            if (task.IsFaulted)
            {
                var e = task.Exception?.GetBaseException();
                string reason = "구글 로그인 실패";
                if (e is GoogleSignIn.SignInException sie)
                    reason = $"구글 로그인 실패: Status={sie.Status} ({(int)sie.Status})";
                else if (e != null)
                    reason = "구글 로그인 실패: " + e.GetType().Name + " " + e.Message;
                Debug.LogError("[Auth] " + reason);
                onDone?.Invoke(false, reason);
                return;
            }
            var credential = GoogleAuthProvider.GetCredential(task.Result.IdToken, null);
            auth.SignInWithCredentialAsync(credential).ContinueWithOnMainThread(t =>
            {
                if (t.IsCanceled || t.IsFaulted) { onDone?.Invoke(false, FirebaseError(t)); return; }
                ApplyUser(auth.CurrentUser, guest: false);
                onDone?.Invoke(true, "구글 로그인 성공");
            });
        });
#else
        onDone?.Invoke(false, "구글 로그인은 Firebase + Google Sign-In 플러그인 설정이 필요합니다.");
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
                onDone?.Invoke(true, "게스트로 시작");
            });
            return;
        }
#endif
        // Firebase 없이도 게스트는 바로 진행 (저장은 로컬로 처리)
        SetGuestLocal();
        onDone?.Invoke(true, "게스트로 시작");
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
        { onDone?.Invoke(false, "이메일 형식이 올바르지 않습니다. (예: name@example.com)"); return false; }
        if (string.IsNullOrEmpty(password) || password.Length < 6)
        { onDone?.Invoke(false, "비밀번호는 6자 이상이어야 합니다."); return false; }
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
            : (guest ? "게스트" : (user != null ? user.Email : ""));
        PlayerPrefs.SetInt(GuestKey, guest ? 1 : 0); // 게스트=로컬저장, 실제계정=클라우드
    }

    static string FirebaseError(System.Threading.Tasks.Task t)
    {
        var e = t.Exception?.GetBaseException();
        return e != null ? "실패: " + e.Message : "실패";
    }
#endif
}
