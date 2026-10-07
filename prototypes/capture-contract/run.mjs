// PROTOTYPE — runs every Capture through ChatGPT and writes results/<run>.json.
//
//   node run.mjs chatgpt [--models gpt-5.6-sol,gpt-5.6-luna] [--runs N] [--only id,id]
//   node run.mjs show        (print instructions + schema, no network)
//
// ChatGPT: opens your browser for Sign in with ChatGPT, keeps tokens in memory only,
// revokes them at the end. Only the (non-secret) host ID and issued client ID are cached.

import http from "node:http";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { exec } from "node:child_process";
import { fileURLToPath } from "node:url";
import { CONTRACT_VERSION, SCHEMA, instructions, parse, check } from "./contract.mjs";
import { CAPTURES } from "./captures.mjs";

const DIR = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const conn = args[0];
const opt = (name, dflt) => { const i = args.indexOf(`--${name}`); return i >= 0 ? args[i + 1] : dflt; };
const runs = Number(opt("runs", "1"));
const only = opt("only", "")?.split(",").filter(Boolean);
const captures = only?.length ? CAPTURES.filter(c => only.includes(c.id)) : CAPTURES;
const today = new Date();
const INSTR = instructions(today);

if (conn === "show") { console.log(INSTR, "\n\n", JSON.stringify(SCHEMA, null, 2)); process.exit(0); }

// ---------- ChatGPT (Responses API, streaming) ----------
const AUTH = "https://auth.openai.com/api/accounts";
const RESOURCE = "https://api.openai.com/v1";
const b64url = buf => buf.toString("base64url");
const CACHE = path.join(DIR, ".siwc-ids.json"); // host ID + issued client ID, not secrets

async function signIn() {
  const ids = fs.existsSync(CACHE) ? JSON.parse(fs.readFileSync(CACHE, "utf8")) : {};
  ids.hostId ??= `urn:uuid:${crypto.randomUUID()}`;
  const verifier = b64url(crypto.randomBytes(32));
  const challenge = b64url(crypto.createHash("sha256").update(verifier).digest());
  const state = b64url(crypto.randomBytes(16)), nonce = b64url(crypto.randomBytes(16));

  const server = http.createServer();
  await new Promise(r => server.listen(0, "127.0.0.1", r));
  const redirect = `http://127.0.0.1:${server.address().port}/auth/callback`;
  const q = new URLSearchParams({
    response_type: "code", redirect_uri: redirect, state, nonce,
    scope: "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
    resource: RESOURCE, code_challenge: challenge, code_challenge_method: "S256",
    client_id: ids.clientId ?? "dynamic_agent_client", ext_agent_host_id: ids.hostId,
  });
  if (!ids.clientId) q.set("agent_name_hint", "Task Widget (prompt prototype)");
  const url = `${AUTH}/authorize?${q}`;
  console.log("Opening browser to sign in with ChatGPT…\nIf it doesn't open:", url);
  exec(`start "" "${url}"`);

  const cb = await new Promise(resolve => server.on("request", (req, res) => {
    const u = new URL(req.url, redirect);
    if (u.pathname !== "/auth/callback") { res.end(); return; }
    res.end("Signed in. You can close this tab and go back to the terminal.");
    resolve(u.searchParams);
  }));
  server.close();
  if (cb.get("error")) throw new Error(`Sign-in failed: ${cb.get("error")}`);
  if (cb.get("state") !== state) throw new Error("state mismatch");
  if (cb.get("client_id")) ids.clientId = cb.get("client_id");
  fs.writeFileSync(CACHE, JSON.stringify(ids, null, 2));

  const tok = await fetch(`${AUTH}/oauth/token`, {
    method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "authorization_code", client_id: ids.clientId,
      code: cb.get("code"), code_verifier: verifier, redirect_uri: redirect, resource: RESOURCE }),
  }).then(r => r.json());
  if (!tok.access_token) throw new Error(`Token exchange failed: ${JSON.stringify(tok)}`);
  return { access: tok.access_token, refresh: tok.refresh_token, clientId: ids.clientId };
}

async function revoke(s) {
  await fetch(`${AUTH}/oauth/revoke`, {
    method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ token: s.refresh, token_type_hint: "refresh_token", client_id: s.clientId }),
  }).catch(() => {});
}

async function callChatGPT(session, model, capture) {
  const res = await fetch(`${RESOURCE}/responses`, {
    method: "POST", signal: AbortSignal.timeout(30_000),
    headers: { authorization: `Bearer ${session.access}`, "content-type": "application/json" },
    body: JSON.stringify({
      model, instructions: INSTR, stream: true, store: false,
      reasoning: { effort: "low" },
      input: [{ role: "user", content: [{ type: "input_text", text: capture }] }],
      text: { format: { type: "json_schema", ...SCHEMA } },
    }),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}: ${(await res.text()).slice(0, 300)}`);
  let text = "", usage;
  const body = await res.text(); // stream arrives almost all at once; read it whole
  for (const line of body.split("\n")) {
    if (!line.startsWith("data:")) continue;
    let ev; try { ev = JSON.parse(line.slice(5)); } catch { continue; }
    if (ev.type === "response.output_text.delta") text += ev.delta;
    if (ev.type === "response.failed") throw new Error(`response.failed: ${JSON.stringify(ev.response?.error)}`);
    if (ev.type === "response.completed") usage = ev.response?.usage;
  }
  return { text, usage };
}

// ---------- run ----------
async function runAll(connection, model, call) {
  const items = [];
  for (let r = 0; r < runs; r++) for (const c of captures) {
    const t0 = performance.now();
    let item = { id: c.id, run: r + 1, capture: c.text };
    try {
      let { text, usage } = await call(c.text);
      let p = parse(text);
      if (!p.ok) { ({ text, usage } = await call(c.text)); p = parse(text); item.retried = true; }
      item = { ...item, raw: text, usage, ...(p.ok ? { tasks: p.tasks.map(t => ({ ...t, issues: check(t) })) } : { error: p.error }) };
    } catch (e) { item.error = String(e.message ?? e); }
    item.ms = Math.round(performance.now() - t0);
    const n = item.tasks?.length;
    console.log(`${connection}/${model} #${r + 1} ${c.id}: ${item.error ?? `${n} task(s)`} ${n !== undefined && n !== c.count ? `(expected ${c.count})` : ""} ${item.ms} ms`);
    items.push(item);
  }
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const file = path.join(DIR, "results", `${stamp}_${connection}_${model.replace(/\W+/g, "-")}.json`);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify({ connection, model, contract: CONTRACT_VERSION,
    startedAt: new Date().toISOString(), instructions: INSTR, items }, null, 2));
  console.log(`→ ${path.relative(DIR, file)}`);
}

if (conn === "chatgpt") {
  const session = await signIn();
  try {
    for (const m of opt("models", "gpt-5.6-sol").split(","))
      await runAll("chatgpt", m, cap => callChatGPT(session, m, cap));
  } finally { await revoke(session); console.log("Tokens revoked."); }
} else {
  console.error("Usage: node run.mjs chatgpt|show [options]"); process.exit(1);
}
await import("./report.mjs");
