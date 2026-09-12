const PROFILE_VERSION = 1;
const CURRENT_CLIENT_VERSION = "1.3.1.9";

const MAX_SOURCE_LENGTH = 8000;
const MAX_TRANSLATION_LENGTH = 12000;
const MAX_NPC_LENGTH = 256;
const MAX_FINGERPRINT_LENGTH = 128;

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      // Sprawdzamy też realne połączenie z D1.
      const result = await env.DB.prepare("SELECT 1 AS ok").first();

      return json({
        ok: result?.ok === 1,
        service: "lorekeeper-cloud",
        profileVersion: PROFILE_VERSION,
        currentClientVersion: CURRENT_CLIENT_VERSION,
      });
    }

    if (
      request.method === "GET" &&
      url.pathname.startsWith("/v1/translations/")
    ) {
      const lookupKey = url.pathname
        .slice("/v1/translations/".length)
        .toLowerCase();

      const allowLibre = url.searchParams.get("allowLibre") === "1";

      return getTranslation(env, lookupKey, allowLibre);
    }

    if (
      request.method === "POST" &&
      url.pathname === "/v1/translations"
    ) {
      return submitTranslation(request, env);
    }

    return json({ error: "not_found" }, 404);
  },
};

async function getTranslation(env, lookupKey, allowLibre) {
  if (!isSha256Hex(lookupKey)) {
    return json({ error: "invalid_lookup_key" }, 400);
  }

  let row;

  if (allowLibre) {
    row = await env.DB.prepare(
      `SELECT
         lookup_key,
         provider,
         translated_text,
         model,
         client_version,
         confirmations
       FROM translations
       WHERE lookup_key = ?
         AND provider IN ('openai', 'libre')
       ORDER BY
         CASE provider
           WHEN 'openai' THEN 2
           ELSE 1
         END DESC,
         confirmations DESC,
         created_at ASC
       LIMIT 1`
    )
      .bind(lookupKey)
      .first();
  } else {
    row = await env.DB.prepare(
      `SELECT
         lookup_key,
         provider,
         translated_text,
         model,
         client_version,
         confirmations
       FROM translations
       WHERE lookup_key = ?
         AND provider = 'openai'
       ORDER BY
         confirmations DESC,
         created_at ASC
       LIMIT 1`
    )
      .bind(lookupKey)
      .first();
  }

  if (!row) {
    return json(
      { hit: false },
      404,
      { "Cache-Control": "no-store" }
    );
  }

  return json(
    {
      hit: true,
      lookupKey: row.lookup_key,
      provider: row.provider,
      translatedText: row.translated_text,
      model: row.model,
      clientVersion: row.client_version,
      confirmations: row.confirmations,
    },
    200,
    { "Cache-Control": "public, max-age=60" }
  );
}

async function submitTranslation(request, env) {
  let body;

  try {
    body = await request.json();
  } catch {
    return json({ error: "invalid_json" }, 400);
  }

  const validationError = validateSubmission(body);

  if (validationError) {
    return json({ error: validationError }, 400);
  }

  const expectedLookupKey = await createLookupKey(body);

  if (expectedLookupKey !== body.lookupKey.toLowerCase()) {
    return json({ error: "lookup_key_mismatch" }, 400);
  }

  const existing = await env.DB.prepare(
    `SELECT translated_text, client_version
     FROM translations
     WHERE lookup_key = ?
       AND provider = ?
     LIMIT 1`
  )
    .bind(body.lookupKey.toLowerCase(), body.provider)
    .first();

  if (!existing) {
    await env.DB.prepare(
      `INSERT INTO translations (
         lookup_key,
         profile_version,
         source_language,
         target_language,
         source_text,
         translated_text,
         npc_name,
         player_sex,
         speaker_sex,
         terminology_fingerprint,
         provider,
         model,
         client_version,
         confirmations,
         created_at,
         last_seen_at
       )
       VALUES (
         ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1,
         CURRENT_TIMESTAMP,
         CURRENT_TIMESTAMP
       )`
    )
      .bind(
        body.lookupKey.toLowerCase(),
        body.profileVersion,
        body.sourceLanguage,
        body.targetLanguage,
        body.sourceText,
        body.translatedText,
        body.npcName,
        body.playerSex,
        body.speakerSex,
        body.terminologyFingerprint,
        body.provider,
        normalizeOptional(body.model),
        normalizeClientVersion(body.clientVersion)
      )
      .run();

    return json(
      { ok: true, action: "inserted" },
      201
    );
  }

  const incomingClientVersion =
    normalizeClientVersion(body.clientVersion);

  const existingClientVersion =
    normalizeClientVersion(existing.client_version) ?? "0.0.0.0";

  if (existing.translated_text === body.translatedText) {
    const confirmedClientVersion =
      incomingClientVersion &&
      compareVersions(
        incomingClientVersion,
        existingClientVersion
      ) > 0
        ? incomingClientVersion
        : existingClientVersion;

    await env.DB.prepare(
      `UPDATE translations
       SET confirmations = confirmations + 1,
           client_version = ?,
           last_seen_at = CURRENT_TIMESTAMP
       WHERE lookup_key = ?
         AND provider = ?`
    )
      .bind(
        confirmedClientVersion,
        body.lookupKey.toLowerCase(),
        body.provider
      )
      .run();

    return json({
      ok: true,
      action: "confirmed",
      clientVersion: confirmedClientVersion,
    });
  }

  // Zmieniony tekst z Nowszej wersji Lorekeepera zastępuje kanoniczny
  // wpis w Cloud. Starsza wersja nigdy nie może cofnąć tej korekty.
  if (
    incomingClientVersion &&
    compareVersions(
      incomingClientVersion,
      CURRENT_CLIENT_VERSION
    ) === 0 &&
    compareVersions(
      incomingClientVersion,
      existingClientVersion
    ) > 0
  ) {
    await env.DB.prepare(
      `UPDATE translations
       SET translated_text = ?,
           model = ?,
           client_version = ?,
           confirmations = 1,
           last_seen_at = CURRENT_TIMESTAMP
       WHERE lookup_key = ?
         AND provider = ?`
    )
      .bind(
        body.translatedText,
        normalizeOptional(body.model),
        incomingClientVersion,
        body.lookupKey.toLowerCase(),
        body.provider
      )
      .run();

    return json({
      ok: true,
      action: "updated_newer_client",
      previousClientVersion: existingClientVersion,
      clientVersion: incomingClientVersion,
    });
  }

  // Ten sam lub starszy klient nie nadpisuje kanonicznego wpisu.
  // Inny wariant trafia do konfliktów do późniejszej moderacji.
  const submittedHash =
    await sha256Hex(body.translatedText);

  await env.DB.prepare(
    `INSERT OR IGNORE INTO translation_conflicts (
       lookup_key,
       provider,
       submitted_translation_hash,
       canonical_translation,
       submitted_translation,
       model,
       client_id,
       client_version,
       created_at
     )
     VALUES (
       ?, ?, ?, ?, ?, ?, ?, ?,
       CURRENT_TIMESTAMP
     )`
  )
    .bind(
      body.lookupKey.toLowerCase(),
      body.provider,
      submittedHash,
      existing.translated_text,
      body.translatedText,
      normalizeOptional(body.model),
      body.clientId,
      incomingClientVersion
    )
    .run();

  return json(
    {
      ok: true,
      action: "conflict_recorded",
      canonicalClientVersion: existingClientVersion,
    },
    202
  );
}

function validateSubmission(body) {
  if (!body || typeof body !== "object") {
    return "invalid_body";
  }

  if (!isSha256Hex(body.lookupKey)) {
    return "invalid_lookup_key";
  }

  if (body.profileVersion !== PROFILE_VERSION) {
    return "unsupported_profile_version";
  }

  if (
    body.sourceLanguage !== "en" ||
    body.targetLanguage !== "pl"
  ) {
    return "unsupported_language_pair";
  }

  if (
    body.provider !== "openai" &&
    body.provider !== "libre"
  ) {
    return "invalid_provider";
  }

  if (
    !isText(
      body.sourceText,
      1,
      MAX_SOURCE_LENGTH
    )
  ) {
    return "invalid_source_text";
  }

  if (
    !isText(
      body.translatedText,
      1,
      MAX_TRANSLATION_LENGTH
    )
  ) {
    return "invalid_translation";
  }

  if (
    !isText(
      body.npcName,
      0,
      MAX_NPC_LENGTH
    )
  ) {
    return "invalid_npc_name";
  }

  if (
    !isText(
      body.terminologyFingerprint,
      1,
      MAX_FINGERPRINT_LENGTH
    )
  ) {
    return "invalid_terminology_fingerprint";
  }

  if (
    !["Unknown", "Male", "Female"].includes(
      body.playerSex
    )
  ) {
    return "invalid_player_sex";
  }

  if (
    !["Unknown", "Male", "Female"].includes(
      body.speakerSex
    )
  ) {
    return "invalid_speaker_sex";
  }

  if (
    typeof body.clientId !== "string" ||
    !/^[a-f0-9]{16,64}$/i.test(body.clientId)
  ) {
    return "invalid_client_id";
  }

  if (!normalizeClientVersion(body.clientVersion)) {
    return "invalid_client_version";
  }

  return null;
}

async function createLookupKey(body) {
  const canonical =
    part(String(body.profileVersion)) +
    part(body.sourceLanguage) +
    part(body.targetLanguage) +
    part(body.playerSex) +
    part(body.speakerSex) +
    part(body.npcName) +
    part(body.terminologyFingerprint) +
    part(body.sourceText);

  return sha256Hex(canonical);
}

function part(value) {
  value = value ?? "";
  return `${value.length}:${value}|`;
}

async function sha256Hex(value) {
  const bytes =
    new TextEncoder().encode(value);

  const digest =
    await crypto.subtle.digest(
      "SHA-256",
      bytes
    );

  return [...new Uint8Array(digest)]
    .map(
      (b) =>
        b.toString(16).padStart(2, "0")
    )
    .join("");
}

function isSha256Hex(value) {
  return (
    typeof value === "string" &&
    /^[a-f0-9]{64}$/i.test(value)
  );
}

function isText(
  value,
  minLength,
  maxLength
) {
  return (
    typeof value === "string" &&
    value.length >= minLength &&
    value.length <= maxLength
  );
}

function normalizeClientVersion(value) {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();

  if (!/^\d+(?:\.\d+){0,3}$/.test(trimmed)) {
    return null;
  }

  const parts = trimmed
    .split(".")
    .map((part) => Number(part));

  while (parts.length < 4) {
    parts.push(0);
  }

  return parts.join(".");
}

function compareVersions(left, right) {
  const a = normalizeClientVersion(left);
  const b = normalizeClientVersion(right);

  if (!a || !b) {
    return 0;
  }

  const aParts = a.split(".").map(Number);
  const bParts = b.split(".").map(Number);

  for (let i = 0; i < 4; i++) {
    if (aParts[i] !== bParts[i]) {
      return aParts[i] > bParts[i] ? 1 : -1;
    }
  }

  return 0;
}

function normalizeOptional(value) {
  if (
    typeof value !== "string" ||
    !value.trim()
  ) {
    return null;
  }

  return value
    .trim()
    .slice(0, 128);
}

function json(
  body,
  status = 200,
  headers = {}
) {
  return new Response(
    JSON.stringify(body),
    {
      status,
      headers: {
        "Content-Type":
          "application/json; charset=utf-8",
        ...headers,
      },
    }
  );
}
