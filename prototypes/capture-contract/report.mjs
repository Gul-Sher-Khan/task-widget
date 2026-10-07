// PROTOTYPE — builds report.html from results/*.json (latest file per connection+model+contract).
//   node report.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { CAPTURES } from "./captures.mjs";

const DIR = path.dirname(fileURLToPath(import.meta.url));
const RES = path.join(DIR, "results");
const files = fs.existsSync(RES) ? fs.readdirSync(RES).filter(f => f.endsWith(".json")).sort() : [];
const latest = new Map();
for (const f of files) {
  const r = JSON.parse(fs.readFileSync(path.join(RES, f), "utf8"));
  latest.set(`${r.connection}|${r.model}|${r.contract}`, { ...r, file: f });
}
const cols = [...latest.values()];
const esc = s => String(s ?? "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);
const median = a => { const s = [...a].sort((x, y) => x - y); return s.length ? s[Math.floor(s.length / 2)] : 0; };

function summary(r) {
  const first = r.items.filter(i => i.run === 1);
  const ok = first.filter(i => CAPTURES.find(c => c.id === i.id)?.count === i.tasks?.length).length;
  const issues = r.items.reduce((n, i) => n + (i.tasks ?? []).reduce((m, t) => m + t.issues.length, 0), 0);
  const errs = r.items.filter(i => i.error).length;
  return `<div class="colhead"><b>${esc(r.connection)}</b> · ${esc(r.model)}<br>
    <span>${ok}/${first.length} counts as expected · ${issues} rule flags · ${errs} errors · median ${(median(r.items.map(i => i.ms)) / 1000).toFixed(1)} s · ${esc(r.contract)}</span></div>`;
}

function cell(r, c) {
  const items = r.items.filter(i => i.id === c.id);
  if (!items.length) return `<td class="muted">not run</td>`;
  const i = items[0];
  const counts = items.map(x => x.error ? "err" : x.tasks.length);
  const stable = new Set(counts).size === 1;
  const bad = !i.error && i.tasks.length !== c.count;
  const tasks = i.error ? `<div class="err">${esc(i.error)}</div>` :
    i.tasks.length === 0 ? `<div class="muted">No Tasks</div>` :
    i.tasks.map(t => `<div class="task">
      <div><span class="pri ${esc(t.priority)}">${esc(t.priority)}</span><span class="eff">${esc(t.effort)}</span>
      <span class="title">${esc(t.title)}</span></div>
      ${t.details ? `<div class="details">${esc(t.details)}</div>` : ""}
      ${t.issues.length ? `<div class="flag">⚑ ${esc(t.issues.join(", "))}</div>` : ""}
    </div>`).join("");
  return `<td class="${bad ? "miss" : ""}">${tasks}
    <div class="meta">${(i.ms / 1000).toFixed(1)} s${i.retried ? " · retried" : ""}${items.length > 1 ? ` · counts over ${items.length} runs: ${counts.join(", ")}${stable ? "" : " ⚠ unstable"}` : ""}</div></td>`;
}

const instr = cols.at(-1)?.instructions ?? "(no runs yet)";
const html = `<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>Capture contract report</title>
<style>
:root{--bg:#fafafa;--fg:#1b1b1f;--muted:#6b6b75;--line:#e3e3e8;--card:#fff;--accent:#2563eb;--miss:#fff4e5;
--high:#c62828;--medium:#b26a00;--low:#2e7d32}
@media (prefers-color-scheme:dark){:root{--bg:#141417;--fg:#ececf1;--muted:#9a9aa6;--line:#2c2c33;--card:#1c1c21;--miss:#3a2a12;
--high:#ef5350;--medium:#ffb74d;--low:#81c784}}
body{margin:0;background:var(--bg);color:var(--fg);font:14px/1.45 "Segoe UI Variable","Segoe UI",system-ui,sans-serif}
main{max-width:1500px;margin:0 auto;padding:24px 16px}
h1{font-size:22px;margin:0 0 4px}p.lead{color:var(--muted);margin:0 0 20px}
details.instr{background:var(--card);border:1px solid var(--line);border-radius:8px;padding:10px 14px;margin-bottom:20px}
details.instr pre{white-space:pre-wrap;font:12.5px/1.5 ui-monospace,Consolas,monospace}
.wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;min-width:${300 + cols.length * 320}px}
th,td{border-bottom:1px solid var(--line);padding:10px;vertical-align:top;text-align:left}
th{position:sticky;top:0;background:var(--bg);font-weight:400}
.colhead span,.meta,.muted,.want{color:var(--muted);font-size:12.5px}
.cap{width:300px}.cap q{display:block;margin:4px 0 6px;font-style:italic}
.probe{font-weight:600}.want{margin-top:4px}
td.miss{background:var(--miss)}
.task{background:var(--card);border:1px solid var(--line);border-radius:6px;padding:6px 8px;margin-bottom:6px}
.pri,.eff{display:inline-block;font-size:11px;text-transform:uppercase;letter-spacing:.04em;margin-right:6px;font-weight:600}
.pri.high{color:var(--high)}.pri.medium{color:var(--medium)}.pri.low{color:var(--low)}.eff{color:var(--muted)}
.title{font-weight:600}.details{color:var(--muted);margin-top:2px}
.flag,.err{color:var(--high);font-size:12.5px;margin-top:2px}
</style></head><body><main>
<h1>Capture → Tasks contract</h1>
<p class="lead">PROTOTYPE for issue #15. Each row is a dictated Capture; each column is a Connection + model. Orange cells got a different number of Tasks than expected. ⚑ marks mechanical rule breaks. Splitting and invented Details need your eye.</p>
<details class="instr"><summary>Instructions sent (latest run)</summary><pre>${esc(instr)}</pre></details>
<div class="wrap"><table><thead><tr><th class="cap">Capture</th>${cols.map(r => `<th>${summary(r)}</th>`).join("")}</tr></thead><tbody>
${CAPTURES.map(c => `<tr><td class="cap"><div class="probe">${esc(c.probe)}</div><q>${esc(c.text)}</q>
<div class="want">Expect ${c.count}: ${esc(c.want)}</div></td>${cols.map(r => cell(r, c)).join("")}</tr>`).join("\n")}
</tbody></table></div></main></body></html>`;
fs.writeFileSync(path.join(DIR, "report.html"), html);
console.log(`report.html: ${cols.length} column(s)`);
