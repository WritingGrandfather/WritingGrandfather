# openaiProxy — OpenAI 키를 클라이언트에서 빼내기 위한 서버 프록시

## 왜 필요한가
`Assets/Resources/ApiKeyConfig.asset`에 진짜 OpenAI 키(`sk-...`)를 넣으면, 그 값은 빌드된
APK/IPA 안에 그대로 박혀서 누구나 앱을 뜯어보면 꺼내갈 수 있습니다. 이 함수는 그 키를
서버(Firebase Secret Manager)에만 두고, Unity는 이 함수를 대신 호출하게 해서 키를
클라이언트에 절대 노출하지 않게 합니다.

Unity → (X-Proxy-Key 공유 비밀값) → `openaiProxy` → (진짜 OpenAI 키) → OpenAI API

## 처음 한 번만 하는 설정

```bash
# 1) Firebase CLI 설치 (이미 있으면 생략)
npm install -g firebase-tools

# 2) 로그인 (본인 Firebase 계정으로, 이 프로젝트에 접근 권한이 있어야 함)
firebase login

# 3) 프로젝트 루트에서 함수 의존성 설치
cd functions
npm install
cd ..

# 4) 두 시크릿 값 등록 (물어보면 값을 붙여넣기)
firebase functions:secrets:set OPENAI_API_KEY
#   → 여기에 진짜 OpenAI 키(sk-...)를 붙여넣는다.

firebase functions:secrets:set PROXY_SHARED_SECRET
#   → 여기는 아무 값이나 직접 만든 긴 임의 문자열(예: openssl rand -hex 32 결과)을 붙여넣는다.
#     이 값이 곧 Unity의 ApiKeyConfig.asset에 넣을 값이다 (OpenAI 키 아님!).

# 5) 배포
firebase deploy --only functions
```

배포가 끝나면 터미널에 `openaiProxy` 함수의 URL이 출력됩니다. 2세대(v2) 함수라
`https://openaiproxy-XXXXXXXXXX-uc.a.run.app` 같은 Cloud Run 형식입니다
(예전 `https://REGION-PROJECT.cloudfunctions.net/NAME` 형식이 아닙니다).

## Unity 쪽 설정

1. 출력된 URL을 다음 두 컴포넌트의 **Proxy Url** 필드에 붙여넣기:
   - `OpenAIHandwritingEvaluator.proxyUrl`
   - `StrokeOrderChecker.proxyUrl`
   (인스펙터에서 직접 설정하거나, 둘 다 씬에 있다면 프리팹으로 만들어 재사용해도 됨)
2. `Assets/Resources/ApiKeyConfig.asset`을 열어 `Api Key` 필드에 **4번에서 만든
   PROXY_SHARED_SECRET 값**을 넣기 (OpenAI 키가 아님에 주의).
   - 이 asset은 gitignore돼 있으므로 팀원마다 각자 로컬에서 값을 채워야 합니다.

## 확인 방법

앱을 실행해 정밀쓰기 화면에서 글자를 쓰고 [완료]를 누르면, Unity Console에
`[OpenAI] AI 분석 원문:` 로그가 찍히면 정상 연결된 것입니다. `Unauthorized`가 뜨면
ApiKeyConfig의 값과 `PROXY_SHARED_SECRET` 시크릿 값이 다른지 확인하세요.

## 참고 — 더 강화하고 싶다면
지금 방식은 "공유 비밀값 하나"로 프록시를 지키는 가장 단순한 형태입니다. 유출 시에도
OpenAI 계정 자체는 안전하고 이 값만 바꾸면 즉시 무효화되지만, 값 자체는 여전히 클라이언트
빌드 안에 있습니다. 더 강한 보호가 필요하면 Firebase **App Check**(디바이스 무결성 검증)를
Unity SDK에 추가해 `openaiProxy`에 `enforceAppCheck: true`를 거는 방법을 고려하세요.
