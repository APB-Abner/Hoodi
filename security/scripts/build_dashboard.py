"""Gera o dashboard executivo "Risco Cidadão" e os relatórios de auditoria.

Entradas:  security/reports/semgrep-*.json, security/evidence/ai-alerts.json
Saídas:    security/dashboard/index.html   (abre direto no navegador, sem internet)
           security/evidence/citizen-risk.json
           security/evidence/taint-audit.md
"""
from __future__ import annotations

import html
import json
import sys
from datetime import datetime
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from taxonomy import (DATA_CATEGORIES, LEVELS, SCORE_SCALE, SINK_TYPES,  # noqa: E402
                      citizen_risk, load_target)

SECURITY_DIR = Path(__file__).resolve().parents[1]
REPORT_DIR = SECURITY_DIR / "reports"
EVIDENCE_DIR = SECURITY_DIR / "evidence"
DASHBOARD = SECURITY_DIR / "dashboard" / "index.html"

TARGETS = {
    "api": ("API hoodi (código real)", "Hoodi.Api, Application, Domain e Infrastructure"),
    "prototipo-vulneravel": ("Protótipo vulnerável", "Eventos do Hoodi Pet com vazamentos propositais"),
    "prototipo-corrigido": ("Protótipo corrigido", "Mesmo fluxo com sanitizadores"),
    "mutacao": ("Teste de mutação", "Cópia do PanicoService com um debug esquecido"),
}


def build() -> dict:
    alerts_file = EVIDENCE_DIR / "ai-alerts.json"
    alerts_doc = json.loads(alerts_file.read_text(encoding="utf-8")) if alerts_file.exists() else {}
    alerts = alerts_doc.get("alerts", [])
    alerts_by_id = {(a["target"], a["finding_id"]): a for a in alerts}

    data = {"generated_at": datetime.now().astimezone().strftime("%d/%m/%Y %H:%M"), "targets": {}}
    for target, (title, subtitle) in TARGETS.items():
        findings = load_target(REPORT_DIR, target)
        risk = citizen_risk(findings)
        exposures = sorted((f for f in findings if f["kind"] != "inventory"),
                           key=lambda f: (-f["risk_points"], f["path"], f["line"]))
        data["targets"][target] = {
            "title": title,
            "subtitle": subtitle,
            "risk": risk,
            "findings": [{
                "rule": f["rule"], "kind": f["kind"], "severity": f["severity"],
                "path": f["path"], "line": f["line"], "code": f["code"],
                "category": f["category"], "sink": f["sink"], "sources": f["source_names"],
                "points": f["risk_points"], "alert": alerts_by_id.get((target, f["id"])),
            } for f in exposures],
        }
    data["ai_sources"] = sorted({a["source"] for a in alerts})
    data["ai_model"] = alerts_doc.get("model", "template")
    return data


def write_audit_markdown(data: dict) -> None:
    lines = ["# Auditoria de PII — Taint Analysis em ação", "",
             f"Gerado em {data['generated_at']} por `security/scripts/run_audit.sh`.", "",
             "| Alvo | Risco Cidadão | Nível | Exposições | Bloqueantes (ERROR) |",
             "|---|---|---|---|---|"]
    for t in data["targets"].values():
        r = t["risk"]
        lines.append(f"| {t['title']} | {r['score']}/100 | {r['level']} | {r['exposures']} | {r['blocking']} |")
    for t in data["targets"].values():
        lines += ["", f"## {t['title']}", "", f"_{t['subtitle']}_", ""]
        if not t["findings"]:
            lines.append("Nenhuma exposição encontrada: os sanitizadores interromperam todos os fluxos.")
            continue
        lines += ["| Arquivo:linha | Regra | Dado (fonte) | Saída (sink) | Pontos | Alerta ao cidadão |",
                  "|---|---|---|---|---|---|"]
        for f in t["findings"]:
            alert = (f["alert"] or {}).get("alerta_cidadao", "").replace("|", "/").replace("\n", " ")
            sources = ", ".join(f["sources"]) or "-"
            lines.append(f"| `{f['path']}:{f['line']}` | `{f['rule']}` | {DATA_CATEGORIES[f['category']]['label']} "
                         f"({sources}) | {SINK_TYPES[f['sink']]['label']} | {f['points']} | {alert} |")
    (EVIDENCE_DIR / "taint-audit.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


PAGE = """<!doctype html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Risco Cidadão hoodi</title>
<style>
:root {
  color-scheme: light;
  --page: #f9f9f7; --surface: #fcfcfb; --ink: #0b0b0b; --ink-2: #52514e; --muted: #898781;
  --grid: #e1e0d9; --axis: #c3c2b7; --ring: rgba(11,11,11,0.10);
  --series: #2a78d6; --series-track: #cde2fb;
  --good: #0ca30c; --warning: #fab219; --serious: #ec835a; --critical: #d03b3b;
  --code-bg: #f0efec;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    color-scheme: dark;
    --page: #0d0d0d; --surface: #1a1a19; --ink: #f0efec; --ink-2: #c3c2b7; --muted: #898781;
    --grid: #2c2c2a; --axis: #383835; --ring: rgba(255,255,255,0.10);
    --series: #3987e5; --series-track: #184f95; --code-bg: #262624;
  }
}
:root[data-theme="dark"] {
  color-scheme: dark;
  --page: #0d0d0d; --surface: #1a1a19; --ink: #f0efec; --ink-2: #c3c2b7; --muted: #898781;
  --grid: #2c2c2a; --axis: #383835; --ring: rgba(255,255,255,0.10);
  --series: #3987e5; --series-track: #184f95; --code-bg: #262624;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--page); color: var(--ink);
  font: 15px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
main { max-width: 1120px; margin: 0 auto; padding: 32px 16px 64px; }
header h1 { font-size: 26px; margin: 0 0 4px; letter-spacing: -0.01em; }
header p { margin: 0; color: var(--ink-2); }
.meta { color: var(--muted); font-size: 13px; margin-top: 6px; }
h2 { font-size: 17px; margin: 0 0 12px; }
.tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(230px, 1fr)); gap: 12px; margin: 24px 0; }
.tile { background: var(--surface); border: 1px solid var(--ring); border-radius: 12px; padding: 16px;
  text-align: left; cursor: pointer; color: inherit; font: inherit; }
.tile[aria-pressed="true"] { outline: 2px solid var(--series); outline-offset: -1px; }
.tile .label { color: var(--ink-2); font-size: 13px; }
.tile .value { font-size: 40px; font-weight: 600; line-height: 1.1; margin: 6px 0 2px; }
.tile .value small { font-size: 15px; color: var(--muted); font-weight: 400; }
.meter { height: 8px; border-radius: 4px; background: var(--grid); overflow: hidden; margin: 10px 0 8px; }
.meter > span { display: block; height: 100%; border-radius: 4px; }
.status { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; font-weight: 600; }
.status svg { flex: none; }
.tile .sub { color: var(--muted); font-size: 12px; margin-top: 4px; }
.panel { background: var(--surface); border: 1px solid var(--ring); border-radius: 12px; padding: 20px; margin-top: 16px; }
.grid2 { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 16px; }
.bars { display: grid; gap: 10px; }
.bar-row { display: grid; grid-template-columns: 150px 1fr 48px; align-items: center; gap: 10px; position: relative; }
.bar-row .name { color: var(--ink-2); font-size: 13px; }
.bar-row .track { height: 22px; position: relative; border-left: 1px solid var(--axis); }
.bar-row .fill { height: 14px; margin-top: 4px; background: var(--series); border-radius: 0 4px 4px 0; min-width: 2px; }
.bar-row .num { font-variant-numeric: tabular-nums; font-size: 13px; text-align: right; }
.bar-row:hover .fill, .bar-row:focus .fill { filter: brightness(1.12); }
.empty { color: var(--muted); font-size: 14px; }
.tooltip { position: fixed; pointer-events: none; background: var(--ink); color: var(--page); padding: 6px 10px;
  border-radius: 6px; font-size: 12px; opacity: 0; transition: opacity .1s; z-index: 10; max-width: 260px; }
.alerts { display: grid; gap: 12px; }
.alert { border: 1px solid var(--ring); border-radius: 10px; padding: 14px 16px; background: var(--page); }
.alert-head { display: flex; justify-content: space-between; gap: 12px; flex-wrap: wrap; align-items: baseline; }
.alert h3 { margin: 0; font-size: 15px; }
.alert .citizen { margin: 8px 0; font-size: 15px; }
.alert .harm { margin: 0 0 8px; color: var(--ink-2); font-size: 13px; }
.alert details { font-size: 13px; color: var(--ink-2); }
.alert summary { cursor: pointer; color: var(--ink); }
.loc { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; color: var(--muted); }
pre { background: var(--code-bg); padding: 10px 12px; border-radius: 8px; overflow-x: auto; font-size: 12px; margin: 8px 0 0; }
.chip { font-size: 11px; border: 1px solid var(--ring); border-radius: 999px; padding: 1px 8px; color: var(--ink-2); }
table { width: 100%; border-collapse: collapse; font-size: 13px; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--grid); vertical-align: top; }
th { color: var(--ink-2); font-weight: 600; }
td.n { font-variant-numeric: tabular-nums; text-align: right; }
.formula { color: var(--ink-2); font-size: 14px; }
.formula code { background: var(--code-bg); padding: 1px 6px; border-radius: 4px; }
.table-wrap { overflow-x: auto; }
@media (max-width: 520px) { .bar-row { grid-template-columns: 100px 1fr 40px; } }
</style>
</head>
<body>
<main>
  <header>
    <h1>Risco Cidadão</h1>
    <p>Quanto os dados pessoais de quem usa o hoodi estão expostos no código, de 0 (nenhuma exposição) a 100.</p>
    <div class="meta" id="meta"></div>
  </header>

  <section class="tiles" id="tiles" aria-label="Alvos auditados"></section>

  <section class="grid2">
    <div class="panel">
      <h2 id="cat-title">Risco por tipo de dado</h2>
      <div class="bars" id="by-category"></div>
    </div>
    <div class="panel">
      <h2>Risco por saída insegura</h2>
      <div class="bars" id="by-sink"></div>
    </div>
  </section>

  <section class="panel">
    <h2>Alertas para o cidadão <span class="chip" id="ai-chip"></span></h2>
    <div class="alerts" id="alerts"></div>
  </section>

  <section class="panel">
    <h2>Tabela de achados</h2>
    <div class="table-wrap"><table id="table"></table></div>
  </section>

  <section class="panel">
    <h2>Inventário de PII mapeado</h2>
    <div class="bars" id="inventory"></div>
  </section>

  <section class="panel formula">
    <h2>Como o score é calculado</h2>
    <p>Cada achado vale <code>peso do dado × fator da saída</code>. Pesos: localização e senha 10, saúde e token 9,
    CPF 8, contato 5, dispositivo 4. Fatores: envio a terceiros e resposta da API 1,0; arquivo 0,8; log 0,7;
    exceção 0,6; console e URL 0,5; dado fixo 0,4; risco latente (record sem ToString) 0,03.</p>
    <p><code>Risco Cidadão = 100 × (1 − e<sup>−soma/__SCALE__</sup>)</code>, com níveis:
    Baixo (&lt; 25), Moderado (25–49), Alto (50–74), Crítico (≥ 75). Achados de severidade ERROR no código
    da API bloqueiam o pipeline.</p>
  </section>
</main>
<div class="tooltip" id="tooltip" role="tooltip"></div>

<script id="data" type="application/json">__DATA__</script>
<script>
const DATA = JSON.parse(document.getElementById('data').textContent);
const CATEGORIES = __CATEGORIES__;
const SINKS = __SINKS__;
const STATUS = {
  'Baixo':    {color: 'var(--good)',     icon: '<path d="M3 8.5l3 3 7-7" fill="none" stroke="currentColor" stroke-width="2"/>'},
  'Moderado': {color: 'var(--warning)',  icon: '<path d="M8 2l7 12H1z" fill="currentColor"/>'},
  'Alto':     {color: 'var(--serious)',  icon: '<path d="M8 2l7 12H1z" fill="currentColor"/>'},
  'Crítico':  {color: 'var(--critical)', icon: '<circle cx="8" cy="8" r="7" fill="currentColor"/><path d="M8 4v5M8 11v1.5" stroke="#fff" stroke-width="2"/>'},
};
const SEVERITY = {critica: 'Crítico', alta: 'Alto', media: 'Moderado', baixa: 'Baixo'};
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const fmt = n => Number(n).toLocaleString('pt-BR', {maximumFractionDigits: 1});
const status = level => `<span class="status" style="color:${STATUS[level].color}">
  <svg width="16" height="16" viewBox="0 0 16 16" aria-hidden="true">${STATUS[level].icon}</svg>
  <span style="color:var(--ink)">${level}</span></span>`;

let current = 'prototipo-vulneravel';
try { current = localStorage.getItem('hoodi-risk-target') || current; } catch (e) {}
if (!DATA.targets[current]) current = 'prototipo-vulneravel';

const tooltip = document.getElementById('tooltip');
function bindTooltip(el, text) {
  const show = e => { tooltip.textContent = text; tooltip.style.opacity = 1; move(e); };
  const move = e => {
    const r = el.getBoundingClientRect();
    const x = e && e.clientX ? e.clientX : r.left + r.width / 2;
    const y = e && e.clientY ? e.clientY : r.top;
    tooltip.style.left = Math.min(x + 12, innerWidth - tooltip.offsetWidth - 8) + 'px';
    tooltip.style.top = (y - tooltip.offsetHeight - 10) + 'px';
  };
  el.addEventListener('mouseenter', show); el.addEventListener('mousemove', move);
  el.addEventListener('focus', () => show()); el.tabIndex = 0;
  el.addEventListener('mouseleave', () => tooltip.style.opacity = 0);
  el.addEventListener('blur', () => tooltip.style.opacity = 0);
}

function bars(container, entries, labels, unit, tipFor) {
  const el = document.getElementById(container);
  el.innerHTML = '';
  if (!entries.length) { el.innerHTML = '<p class="empty">Nenhuma exposição neste alvo.</p>'; return; }
  const max = Math.max(...entries.map(e => e[1]));
  for (const [key, value] of entries) {
    const row = document.createElement('div');
    row.className = 'bar-row';
    row.innerHTML = `<span class="name">${esc(labels[key]?.label || key)}</span>
      <span class="track"><span class="fill" style="display:block;width:${(value / max * 100).toFixed(1)}%"></span></span>
      <span class="num">${fmt(value)}</span>`;
    bindTooltip(row, tipFor(key, value));
    el.appendChild(row);
  }
}

function render() {
  const t = DATA.targets[current];
  const ai = DATA.ai_sources.includes('ia');
  document.getElementById('meta').textContent =
    `Gerado em ${DATA.generated_at} · Semgrep (regras próprias de PII e taint) · alertas ${ai ? `traduzidos por IA (${DATA.ai_model})` : 'gerados por template, sem IA'}`;
  document.getElementById('ai-chip').textContent = ai ? 'traduzidos por IA' : 'template (sem IA)';

  const tiles = document.getElementById('tiles');
  tiles.innerHTML = '';
  for (const [key, target] of Object.entries(DATA.targets)) {
    const r = target.risk;
    const b = document.createElement('button');
    b.className = 'tile'; b.setAttribute('aria-pressed', key === current);
    b.innerHTML = `<div class="label">${esc(target.title)}</div>
      <div class="value">${r.score}<small> /100</small></div>
      <div class="meter" role="img" aria-label="${r.score} de 100"><span style="width:${r.score}%;background:${STATUS[r.level].color}"></span></div>
      ${status(r.level)}
      <div class="sub">${r.exposures} exposições · ${r.blocking} bloqueantes · ${esc(target.subtitle)}</div>`;
    b.onclick = () => { current = key; try { localStorage.setItem('hoodi-risk-target', key); } catch (e) {} render(); };
    tiles.appendChild(b);
  }

  const byCat = Object.entries(t.risk.by_category).sort((a, b) => b[1] - a[1]);
  bars('by-category', byCat, CATEGORIES, 'pts', (k, v) => `${CATEGORIES[k].label}: ${fmt(v)} pontos — ${CATEGORIES[k].why}`);
  const bySink = Object.entries(t.risk.by_sink).sort((a, b) => b[1] - a[1]);
  bars('by-sink', bySink, SINKS, 'pts', (k, v) => `${SINKS[k].label}: ${fmt(v)} pontos (fator ${SINKS[k].factor})`);
  const inv = Object.entries(t.risk.inventory).sort((a, b) => b[1] - a[1]);
  bars('inventory', inv, CATEGORIES, 'campos', (k, v) => `${v} campo(s) de ${CATEGORIES[k]?.label || k} declarados`);

  const alerts = document.getElementById('alerts');
  alerts.innerHTML = t.findings.length ? '' : '<p class="empty">Nenhum alerta: todos os fluxos de dado pessoal passam por sanitizadores.</p>';
  for (const f of t.findings) {
    const a = f.alert || {};
    const level = SEVERITY[a.gravidade] || 'Moderado';
    const card = document.createElement('article');
    card.className = 'alert';
    card.innerHTML = `<div class="alert-head"><h3>${esc(a.titulo || f.rule)}</h3>${status(level)}</div>
      <p class="citizen">${esc(a.alerta_cidadao || '')}</p>
      <p class="harm">${esc(a.dano_real || '')}</p>
      <details><summary>Detalhes técnicos e correção</summary>
        <p class="loc">${esc(f.path)}:${f.line} · ${esc(f.rule)} · ${esc(CATEGORIES[f.category].label)} → ${esc(SINKS[f.sink].label)}</p>
        <pre>${esc(f.code)}</pre>
        <p>${esc(a.correcao || '')}</p>
        ${a.patch_csharp ? `<pre>${esc(a.patch_csharp)}</pre>` : ''}
      </details>`;
    alerts.appendChild(card);
  }

  const table = document.getElementById('table');
  table.innerHTML = `<thead><tr><th>Arquivo:linha</th><th>Dado</th><th>Saída</th><th>Severidade</th><th class="n">Pontos</th></tr></thead>
    <tbody>${t.findings.map(f => `<tr><td class="loc">${esc(f.path)}:${f.line}</td>
      <td>${esc(CATEGORIES[f.category].label)}${f.sources.length ? ' (' + esc(f.sources.join(', ')) + ')' : ''}</td>
      <td>${esc(SINKS[f.sink].label)}</td><td>${f.severity}</td><td class="n">${fmt(f.points)}</td></tr>`).join('')
      || '<tr><td colspan="5" class="empty">Sem achados.</td></tr>'}</tbody>`;
}
render();
</script>
</body>
</html>
"""


def write_dashboard(data: dict) -> None:
    page = (PAGE
            .replace("__DATA__", json.dumps(data, ensure_ascii=False).replace("</", "<\\/"))
            .replace("__CATEGORIES__", json.dumps(DATA_CATEGORIES, ensure_ascii=False))
            .replace("__SINKS__", json.dumps(SINK_TYPES, ensure_ascii=False))
            .replace("__SCALE__", f"{SCORE_SCALE:g}"))
    DASHBOARD.parent.mkdir(parents=True, exist_ok=True)
    DASHBOARD.write_text(page, encoding="utf-8")


def main() -> None:
    data = build()
    EVIDENCE_DIR.mkdir(parents=True, exist_ok=True)
    summary = {k: {"title": v["title"], **v["risk"]} for k, v in data["targets"].items()}
    (EVIDENCE_DIR / "citizen-risk.json").write_text(
        json.dumps({"generated_at": data["generated_at"], "levels": LEVELS, "targets": summary},
                   ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    write_audit_markdown(data)
    write_dashboard(data)
    for target in data["targets"].values():
        r = target["risk"]
        print(f"  {target['title']:<28} Risco Cidadão {r['score']:>3}/100  {r['level']:<9} "
              f"({r['exposures']} exposições, {r['blocking']} bloqueantes)")
    print(f"  dashboard -> {DASHBOARD.relative_to(SECURITY_DIR.parent)}")


if __name__ == "__main__":
    main()
