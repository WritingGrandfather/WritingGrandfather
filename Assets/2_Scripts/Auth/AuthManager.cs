using System;
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
    [SerializeField] string googleWebClientId = "";
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
        GoogleSignIn.Configuration = new GoogleSignInConfiguration
        {
            WebClientId = googleWebClientId,
            RequestIdToken = true,
            UseGameSignIn = false
        };
        GoogleSignIn.DefaultInstance.SignIn().ContinueWithOnMainThread(task =>
        {
            if (task.IsCanceled || task.IsFaulted) { onDone?.Invoke(false, "구글 로그인 취소/실패"); return; }
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
        // Firebase 없이도 게스트는 바로 진행 (익명, 저장은 로컬로 처리)
        IsSignedIn = true; IsGuest = true; UserId = "guest"; DisplayName = "게스트";
        onDone?.Invoke(true, "게스트로 시작");
    }

    public void SignOut()
    {
#if FIREBASE_ENABLED
        if (auth != null) auth.SignOut();
#endif
        IsSignedIn = false; IsGuest = false; UserId = ""; DisplayName = "";
    }

    // ── 내부 헬퍼 ───────────────────────────────────────────────────
    bool ValidateInput(string email, string password, Action<bool, string> onDone)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
        { onDone?.Invoke(false, "이메일을 올바르게 입력하세요."); return false; }
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
    }

    static string FirebaseError(System.Threading.Tasks.Task t)
    {
        var e = t.Exception?.GetBaseException();
        return e != null ? "실패: " + e.Message : "실패";
    }
#endif
}
