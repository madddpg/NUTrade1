#!/usr/bin/env node
/**
 * Seeds the NUTrade Firestore project with real, live auction data.
 *
 * Talks only to public endpoints — Firebase Auth REST and the deployed callable
 * Functions — so it needs no service-account key on your machine. The privileged
 * work happens server-side inside `seedDevData`, which is gated on an admin claim.
 *
 * Steps:
 *   1. Sign in (or create) the admin account with email + password.
 *   2. POST to `bootstrapAdmin` with the Secret Manager value, granting that
 *      account `role: admin` and `verified: true`.
 *   3. Re-exchange the refresh token so the new claims are actually in the ID token.
 *   4. Call `seedDevData`, which writes five student accounts and four live auctions
 *      with their bid ladders.
 *
 * Usage:
 *   node scripts/seed.mjs \
 *     --admin-email you@nu-lipa.edu.ph \
 *     --admin-password "..." \
 *     --bootstrap-secret "<ADMIN_BOOTSTRAP_SECRET>" \
 *     --seed-password "..."
 *
 * Every flag can also come from the environment: NUTRADE_ADMIN_EMAIL,
 * NUTRADE_ADMIN_PASSWORD, NUTRADE_BOOTSTRAP_SECRET, NUTRADE_SEED_PASSWORD.
 */

const PROJECT_ID = "nutrade-a25c7";
const API_KEY = "AIzaSyDXWnLMuCkcnVUeNRnJLmeRVVXDZ7KXWXo";
const REGION = "asia-southeast1";

const IDENTITY = "https://identitytoolkit.googleapis.com/v1/accounts";
const SECURE_TOKEN = "https://securetoken.googleapis.com/v1/token";
const fnUrl = (name) => `https://${REGION}-${PROJECT_ID}.cloudfunctions.net/${name}`;

function arg(flag, envVar) {
  const index = process.argv.indexOf(`--${flag}`);
  if (index !== -1 && process.argv[index + 1]) return process.argv[index + 1];
  return process.env[envVar];
}

function fail(message) {
  console.error(`\n  ${message}\n`);
  process.exit(1);
}

async function postJson(url, body, headers = {}) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  const text = await response.text();
  let json;
  try {
    json = JSON.parse(text);
  } catch {
    json = { raw: text };
  }
  return { ok: response.ok, status: response.status, json };
}

/** Signs in, creating the account first if it does not exist yet. */
async function signInOrSignUp(email, password) {
  let result = await postJson(`${IDENTITY}:signInWithPassword?key=${API_KEY}`, {
    email,
    password,
    returnSecureToken: true,
  });

  if (result.ok) return result.json;

  const code = result.json?.error?.message ?? "";
  if (code === "CONFIGURATION_NOT_FOUND") {
    fail(
      "Firebase Authentication has never been initialised on this project.\n" +
        "  Open the console, go to Authentication, click Get started, and enable the\n" +
        "  Email/Password provider. Then re-run this script.\n" +
        `  https://console.firebase.google.com/project/${PROJECT_ID}/authentication/providers`
    );
  }

  if (code !== "EMAIL_NOT_FOUND" && code !== "INVALID_LOGIN_CREDENTIALS") {
    fail(`Could not sign in as ${email}: ${code || JSON.stringify(result.json)}`);
  }

  console.log(`  no account for ${email} yet, creating it`);
  result = await postJson(`${IDENTITY}:signUp?key=${API_KEY}`, {
    email,
    password,
    returnSecureToken: true,
  });

  if (!result.ok) {
    const signUpCode = result.json?.error?.message ?? "";
    if (signUpCode === "OPERATION_NOT_ALLOWED") {
      fail(
        "The Email/Password provider is not enabled on this project.\n" +
          `  https://console.firebase.google.com/project/${PROJECT_ID}/authentication/providers`
      );
    }
    fail(`Could not create ${email}: ${signUpCode || JSON.stringify(result.json)}`);
  }

  return result.json;
}

/** Re-exchanges a refresh token so newly granted custom claims land in the ID token. */
async function refreshIdToken(refreshToken) {
  const response = await fetch(`${SECURE_TOKEN}?key=${API_KEY}`, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "refresh_token", refresh_token: refreshToken }),
  });
  if (!response.ok) fail(`Token refresh failed: ${await response.text()}`);
  return response.json();
}

function readClaims(idToken) {
  const payload = idToken.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
  return JSON.parse(Buffer.from(payload, "base64").toString("utf8"));
}

async function main() {
  const adminEmail = arg("admin-email", "NUTRADE_ADMIN_EMAIL");
  const adminPassword = arg("admin-password", "NUTRADE_ADMIN_PASSWORD");
  const bootstrapSecret = arg("bootstrap-secret", "NUTRADE_BOOTSTRAP_SECRET");
  const seedPassword = arg("seed-password", "NUTRADE_SEED_PASSWORD") ?? adminPassword;
  const endsInMinutesRaw = arg("ends-in-minutes", "NUTRADE_SEED_ENDS_IN_MINUTES");

  if (!adminEmail || !adminPassword || !bootstrapSecret) {
    fail(
      "Missing arguments. Required:\n" +
        "    --admin-email       the account to promote to admin and seed from\n" +
        "    --admin-password    its password (the account is created if absent)\n" +
        "    --bootstrap-secret  the ADMIN_BOOTSTRAP_SECRET value from Secret Manager\n" +
        "  Optional:\n" +
        "    --seed-password     password for the five seeded student accounts\n" +
        "    --ends-in-minutes   override every auction's end time; pass a negative value\n" +
        "                        to seed already-ended auctions and watch\n" +
        "                        closeExpiredAuctions settle them on its next 5-min run"
    );
  }

  let endsInMinutes;
  if (endsInMinutesRaw !== undefined) {
    endsInMinutes = Number(endsInMinutesRaw);
    if (!Number.isFinite(endsInMinutes)) fail("--ends-in-minutes must be a number.");
  }

  if (seedPassword.length < 8) fail("The seed password must be at least 8 characters.");

  console.log(`\nSeeding ${PROJECT_ID}\n`);

  console.log("1. signing in as the admin account");
  const session = await signInOrSignUp(adminEmail, adminPassword);
  console.log(`   uid ${session.localId}`);

  console.log("2. granting the admin claim via bootstrapAdmin");
  const bootstrap = await postJson(
    fnUrl("bootstrapAdmin"),
    { email: adminEmail },
    { "X-Bootstrap-Secret": bootstrapSecret }
  );
  if (!bootstrap.ok) {
    fail(
      bootstrap.status === 401
        ? "bootstrapAdmin rejected the secret. Check the value in Secret Manager."
        : `bootstrapAdmin failed (${bootstrap.status}): ${JSON.stringify(bootstrap.json)}`
    );
  }
  console.log("   role=admin verified=true");

  console.log("3. refreshing the ID token so the new claims take effect");
  const refreshed = await refreshIdToken(session.refreshToken);
  const claims = readClaims(refreshed.id_token);
  if (claims.role !== "admin") {
    fail(`The refreshed token still has no admin claim (role=${claims.role}). Try re-running.`);
  }

  console.log("4. calling seedDevData");
  const payload = { password: seedPassword };
  if (endsInMinutes !== undefined) {
    payload.endsInMinutes = endsInMinutes;
    console.log(`   overriding every auction end time to now ${endsInMinutes >= 0 ? "+" : ""}${endsInMinutes} min`);
  }
  const seeded = await postJson(
    fnUrl("seedDevData"),
    { data: payload },
    { Authorization: `Bearer ${refreshed.id_token}` }
  );

  if (!seeded.ok || seeded.json?.error) {
    fail(`seedDevData failed: ${JSON.stringify(seeded.json?.error ?? seeded.json)}`);
  }

  const result = seeded.json.result;
  console.log(`\nSeeded ${result.listings.length} live auctions and ${result.accounts.length} student accounts.\n`);
  console.log("  Sign in to the app as any of these (password = your --seed-password):");
  for (const account of result.accounts) {
    console.log(`    ${account.email.padEnd(32)} ${account.displayName}`);
  }
  console.log(`\n  Listings: ${result.listings.join(", ")}`);
  console.log(
    "\n  Note: seeded students are pre-verified. A real signup stays unverified\n" +
      "  until the student enters the six-digit code emailed to them.\n"
  );
}

main().catch((err) => fail(err?.stack ?? String(err)));
