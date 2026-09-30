#!/usr/bin/env node
/**
 * Checks whether the DNS records Brevo needs for NUTrade's email are live yet.
 *
 * Brevo's dashboard only tells you pass/fail after you click "Authenticate this domain";
 * this shows which individual record is missing, which is usually the fastest way to spot
 * the classic Cloudflare mistake of leaving a record proxied (orange cloud) instead of
 * DNS-only, or a provider that silently appended the domain to an already-absolute name.
 *
 * Brevo asks for three records on the sending domain:
 *   brevo-code.<domain>       TXT   brevo-code:<32 hex chars>   (ownership)
 *   mail._domainkey.<domain>  TXT   k=rsa; p=...                (DKIM)
 *   <domain>                  TXT   v=spf1 include:spf.brevo.com mp:all  (SPF)
 *
 * Usage: node scripts/check-email-dns.mjs [domain]
 */
import { Resolver } from "node:dns/promises";

// slice(2) drops the node binary and the script path — both contain dots and would
// otherwise be mistaken for the domain.
const args = process.argv.slice(2);
const domain = args.find((a) => !a.startsWith("-")) ?? "nutrade.com";

// Query a public resolver directly so a stale local cache can't show a false negative.
const resolver = new Resolver();
resolver.setServers(["8.8.8.8", "1.1.1.1"]);

const ok = (m) => console.log("  OK    " + m);
const missing = (m) => { console.log("  MISS  " + m); process.exitCode = 1; };

async function txt(name) {
  try {
    return (await resolver.resolveTxt(name)).map((chunks) => chunks.join(""));
  } catch {
    return [];
  }
}

console.log(`\nBrevo email DNS for ${domain}\n`);

console.log("1. domain ownership (brevo-code TXT)");
const code = await txt(`brevo-code.${domain}`);
code.some((r) => r.includes("brevo-code:"))
  ? ok(code.find((r) => r.includes("brevo-code:")))
  : missing(`no brevo-code TXT at brevo-code.${domain} (copy the exact value from Brevo)`);

console.log("\n2. DKIM");
const dkim = await txt(`mail._domainkey.${domain}`);
dkim.some((r) => r.includes("p="))
  ? ok(`mail._domainkey present (${dkim[0].length} chars)`)
  : missing(`no DKIM TXT at mail._domainkey.${domain}`);

console.log("\n3. SPF on the domain itself");
const spf = await txt(domain);
const spfRecord = spf.find((r) => r.startsWith("v=spf1"));
if (!spfRecord) {
  missing(`no SPF TXT at ${domain} (expecting v=spf1 include:spf.brevo.com mp:all)`);
} else if (!spfRecord.includes("spf.brevo.com")) {
  // Only one SPF record is allowed per domain, so an existing one must be edited to add
  // the include rather than a second one being added beside it.
  missing(`SPF exists but doesn't include Brevo — add include:spf.brevo.com to "${spfRecord}"`);
} else {
  ok(spfRecord);
}

console.log("\n4. DMARC (optional, but improves deliverability)");
const dmarc = await txt(`_dmarc.${domain}`);
dmarc.some((r) => r.startsWith("v=DMARC1"))
  ? ok(dmarc.find((r) => r.startsWith("v=DMARC1")))
  : console.log(`  none  no _dmarc.${domain} — fine to skip, worth adding before launch`);

console.log(
  process.exitCode
    ? "\nNot ready. Add the missing records in your DNS provider, set each to DNS only (grey\n" +
      "cloud in Cloudflare, not proxied), then click Authenticate this domain in Brevo.\n"
    : "\nAll required records are live. Authenticate the domain in Brevo if you have not,\n" +
      "then set BREVO_SENDER_EMAIL in functions/.env and redeploy the mailing functions.\n"
);
