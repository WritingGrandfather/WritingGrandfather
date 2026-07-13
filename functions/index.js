const { onRequest } = require("firebase-functions/v2/https");
const { defineSecret } = require("firebase-functions/params");
const logger = require("firebase-functions/logger");

// 두 시크릿 모두 Firebase Secret Manager에 저장한다 (README.md 참고):
//   firebase functions:secrets:set OPENAI_API_KEY
//   firebase functions:secrets:set PROXY_SHARED_SECRET
// OPENAI_API_KEY  : 진짜 OpenAI 키. 이 함수 밖으로는 절대 나가지 않는다.
// PROXY_SHARED_SECRET : Unity 클라이언트가 이 프록시를 호출할 때 증명하는 값
//                        (OpenAI 키가 아니라 우리가 만든 임의의 문자열).
//                        유출돼도 OpenAI 계정 자체는 안전하고, 이 값만 바꾸면 즉시 무효화된다.
const OPENAI_API_KEY = defineSecret("OPENAI_API_KEY");
const PROXY_SHARED_SECRET = defineSecret("PROXY_SHARED_SECRET");

const OPENAI_ENDPOINT = "https://api.openai.com/v1/chat/completions";

/**
 * Unity(OpenAIHandwritingEvaluator / StrokeOrderChecker)가 보내는 chat/completions
 * 요청 바디를 그대로 받아 OpenAI로 대신 전달하고, 응답을 그대로 돌려준다.
 * 클라이언트는 OpenAI API 키를 전혀 몰라도 되고, 대신 이 함수 전용 공유 비밀값만 보낸다.
 */
exports.openaiProxy = onRequest(
  { secrets: [OPENAI_API_KEY, PROXY_SHARED_SECRET], cors: false, timeoutSeconds: 60 },
  async (req, res) => {
    if (req.method !== "POST") {
      res.status(405).send("POST only");
      return;
    }

    const clientKey = req.get("X-Proxy-Key");
    if (!clientKey || clientKey !== PROXY_SHARED_SECRET.value()) {
      logger.warn("[openaiProxy] 잘못된 X-Proxy-Key로 접근 시도");
      res.status(401).send("Unauthorized");
      return;
    }

    try {
      const upstream = await fetch(OPENAI_ENDPOINT, {
        method: "POST",
        headers: {
          "content-type": "application/json",
          "Authorization": `Bearer ${OPENAI_API_KEY.value()}`,
        },
        // Unity가 만든 바디를 그대로 전달 - 여기서는 내용을 검사/가공하지 않는다.
        body: JSON.stringify(req.body),
      });

      const text = await upstream.text();
      res.status(upstream.status).set("content-type", "application/json").send(text);
    } catch (err) {
      logger.error("[openaiProxy] OpenAI 요청 실패", err);
      res.status(502).json({ error: "upstream_request_failed", message: String(err) });
    }
  }
);
