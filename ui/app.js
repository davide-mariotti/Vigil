"use strict";
// Vigil's window. The exe sends: init (once per page), settings (when they change), data (every second).
// The page sends back: ready, set, fan, admin, restart, pawnio, open. Nothing here touches the network.

const $ = id => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const send = m => host && host.postMessage(m);
const icon = (n, cls) => `<svg class="i ${cls || ""}" viewBox="0 0 24 24" aria-hidden="true">${ICONS[n] || ICONS.layers}</svg>`;
const esc = s => String(s == null ? "" : s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
const HISTORY = 600;

// ── words ────────────────────────────────────────────────────────────────
const TEXT = {
  it: {
    mode: { full: "Modalità completa", base: "Modalità base", nodriver: "Driver mancante", hot: "Ventole al 100%" },
    kicker: "Il tuo PC · aggiornato ogni secondo",
    slots: (n, s) => `${n} slot su ${s}`,
    disks: n => n === 1 ? "1 disco" : `${n} dischi`,
    base: ["Modalità base", "Temperatura e consumi della CPU, scheda madre, ventole e dischi si leggono solo da amministratore.", "Riavvia come amministratore"],
    nodriver: ["Manca il driver PawnIO", "Senza, CPU, scheda madre e ventole restano mute. Installalo dal sito ufficiale, poi riavvia Vigil.", "Scarica PawnIO", "Installato: riavvia"],
    hot: (l) => ["Ventole al 100%", `CPU o GPU hanno superato i ${l} °C: le ventole che comandi vanno a tutta finché non scendono di 5 gradi.`],
    kpi: { cpu: "Processore", gpu: "Scheda video", ram: "Memoria", power: "Consumo", load: "carico", free: "liberi", used: "in uso", hotspot: "hot spot", est: "CPU + GPU", needAdmin: "Carico totale: temperatura e watt solo da amministratore" },
    fanKicker: "Ventole",
    fanTitle: 'Decidi tu quanto <span class="serif grad">girano</span>',
    fanSide: "Auto le lascia al BIOS. Fisso tiene una velocità. Curva segue la temperatura che scegli.",
    fanModes: ["Auto", "Fisso", "Curva"],
    fanAuto: gpu => gpu ? "La velocità la decide il driver della scheda video, come sempre." : "La velocità la decide il BIOS, come se Vigil non ci fosse.",
    fanFixed: "Sempre a questa velocità, qualunque cosa succeda. Tranne un'emergenza.",
    fanSource: "Segue",
    fanByBios: gpu => gpu ? "Decide il driver" : "Decide il BIOS",
    fanByVigil: "Comandata da Vigil",
    fanSafety: "Al 100% per sicurezza",
    fanNone: "Le ventole della scheda madre si comandano in modalità completa: riavvia Vigil da amministratore.",
    fanNote: (min, l) => `Mai sotto il ${min}%. Se CPU o GPU superano ${l} °C, le ventole che comandi vanno al 100% finché non scendono di 5 gradi. Quando chiudi Vigil, o se va in crash, tornano al BIOS.`,
    rename: "Clicca per darle un nome",
    hwKicker: "Sensori",
    hwTitle: 'Tutto quello che il PC <span class="serif grad">racconta</span>',
    hwSide: "Clicca un sensore per vederne gli ultimi 10 minuti.",
    kinds: { cpu: "Processore", gpu: "Scheda video", memory: "Memoria", board: "Scheda madre", superio: "Chip dei sensori", ec: "Embedded controller", storage: "Disco", network: "Rete", battery: "Batteria", psu: "Alimentatore", cooler: "Raffreddamento", power: "Misuratore", other: "Altro" },
    types: { Temperature: "Temperature", Fan: "Ventole", Control: "Velocità comandata", Power: "Consumi", Load: "Carico", Clock: "Frequenze", Voltage: "Tensioni", Current: "Correnti", Data: "Dati", SmallData: "Dati", Throughput: "Velocità", Level: "Livelli", Energy: "Energia", Factor: "Moltiplicatori", Frequency: "Frequenze", Flow: "Flusso", Noise: "Rumore", Humidity: "Umidità", TimeSpan: "Tempo", Timing: "Timing" },
    names: { "Total Memory": "RAM", "Virtual Memory": "Memoria virtuale" },
    now: "ora", ago10: "10 min fa", min: "min", avg: "media", max: "max",
    secAgo: s => s < 60 ? `${s} s fa` : `${Math.round(s / 60)} min fa`,
    settings: "Impostazioni",
    startup: "Avvia con Windows",
    startupAdmin: "Da amministratore: tutti i sensori e le tue ventole a ogni accensione, senza la conferma UAC (attività pianificata di Windows).",
    startupBase: "Parte in modalità base. Per avere tutti i sensori all'accensione, attivalo da amministratore.",
    startupPortable: "Versione portable: le impostazioni stanno in Vigil.ini, accanto all'exe, e Vigil non parte da solo.",
    alerts: "Avvisi di temperatura",
    limit: "Limite per CPU e GPU",
    alertsText: "Un avviso quando CPU o GPU restano sopra il limite per più di un minuto. Lo stesso limite manda al 100% le ventole che comandi, anche con gli avvisi spenti.",
    links: ["Codice sorgente", "Offrimi un caffè"],
    about: v => `Vigil ${v} · licenza MIT · i sensori li legge LibreHardwareMonitorLib, il driver è PawnIO. Niente internet, niente telemetria.`,
    footer: v => `Vigil ${v} · fatto da Davide Mariotti · i sensori li legge LibreHardwareMonitorLib`,
  },
  en: {
    mode: { full: "Full mode", base: "Base mode", nodriver: "Driver missing", hot: "Fans at 100%" },
    kicker: "Your PC · updated every second",
    slots: (n, s) => `${n} of ${s} slots`,
    disks: n => n === 1 ? "1 disk" : `${n} disks`,
    base: ["Base mode", "CPU temperature and power, motherboard, fans and disks can only be read as administrator.", "Restart as administrator"],
    nodriver: ["The PawnIO driver is missing", "Without it CPU, motherboard and fans stay silent. Install it from the official site, then restart Vigil.", "Get PawnIO", "Installed: restart"],
    hot: (l) => ["Fans at 100%", `CPU or GPU went over ${l} °C: the fans you command run flat out until it's 5 degrees under.`],
    kpi: { cpu: "Processor", gpu: "Graphics card", ram: "Memory", power: "Power draw", load: "load", free: "free", used: "in use", hotspot: "hot spot", est: "CPU + GPU", needAdmin: "Total load: temperature and watts need administrator" },
    fanKicker: "Fans",
    fanTitle: 'You decide how fast they <span class="serif grad">spin</span>',
    fanSide: "Auto leaves them to the BIOS. Fixed holds one speed. Curve follows the temperature you pick.",
    fanModes: ["Auto", "Fixed", "Curve"],
    fanAuto: gpu => gpu ? "The graphics driver decides the speed, as usual." : "The BIOS decides the speed, as if Vigil weren't there.",
    fanFixed: "Always this speed, whatever happens. Except an emergency.",
    fanSource: "Follows",
    fanByBios: gpu => gpu ? "Driver decides" : "BIOS decides",
    fanByVigil: "Driven by Vigil",
    fanSafety: "At 100% for safety",
    fanNone: "Motherboard fans can be commanded in full mode: restart Vigil as administrator.",
    fanNote: (min, l) => `Never under ${min}%. If CPU or GPU go over ${l} °C, the fans you command go to 100% until it's 5 degrees under. When you close Vigil, or if it crashes, they go back to the BIOS.`,
    rename: "Click to give it a name",
    hwKicker: "Sensors",
    hwTitle: 'Everything your PC <span class="serif grad">tells you</span>',
    hwSide: "Click a sensor to see its last 10 minutes.",
    kinds: { cpu: "Processor", gpu: "Graphics card", memory: "Memory", board: "Motherboard", superio: "Sensor chip", ec: "Embedded controller", storage: "Disk", network: "Network", battery: "Battery", psu: "Power supply", cooler: "Cooling", power: "Power meter", other: "Other" },
    types: { Temperature: "Temperatures", Fan: "Fans", Control: "Fan duty", Power: "Power", Load: "Load", Clock: "Clocks", Voltage: "Voltages", Current: "Currents", Data: "Data", SmallData: "Data", Throughput: "Throughput", Level: "Levels", Energy: "Energy", Factor: "Multipliers", Frequency: "Frequencies", Flow: "Flow", Noise: "Noise", Humidity: "Humidity", TimeSpan: "Time", Timing: "Timings" },
    names: { "Total Memory": "RAM", "Virtual Memory": "Virtual memory" },
    now: "now", ago10: "10 min ago", min: "min", avg: "avg", max: "max",
    secAgo: s => s < 60 ? `${s} s ago` : `${Math.round(s / 60)} min ago`,
    settings: "Settings",
    startup: "Start with Windows",
    startupAdmin: "As administrator: every sensor and your fans at every logon, no UAC prompt (a Windows scheduled task).",
    startupBase: "Starts in base mode. For every sensor at logon, turn it on as administrator.",
    startupPortable: "Portable version: settings live in Vigil.ini, next to the exe, and Vigil doesn't start by itself.",
    alerts: "Temperature alerts",
    limit: "Limit for CPU and GPU",
    alertsText: "A warning when the CPU or GPU stays over the limit for more than a minute. The same limit sends the fans you command to 100%, even with alerts off.",
    links: ["Source code", "Buy me a coffee"],
    about: v => `Vigil ${v} · MIT license · sensors read by LibreHardwareMonitorLib, driver by PawnIO. No internet, no telemetry.`,
    footer: v => `Vigil ${v} · made by Davide Mariotti · sensors read by LibreHardwareMonitorLib`,
  },
};
let T = TEXT.en, locale = "en-US";

const KIND = { Cpu: ["cpu", "cpu"], GpuNvidia: ["gpu", "gpu"], GpuAmd: ["gpu", "gpu"], GpuIntel: ["gpu", "gpu"], Memory: ["memory-stick", "memory"],
  Motherboard: ["circuit-board", "board"], SuperIO: ["microchip", "superio"], EmbeddedController: ["microchip", "ec"], Storage: ["hard-drive", "storage"],
  Network: ["network", "network"], Battery: ["battery", "battery"], Psu: ["plug", "psu"], Cooler: ["fan", "cooler"], PowerMonitor: ["zap", "power"] };
const TYPES = ["Temperature", "Fan", "Control", "Power", "Load", "Clock", "Voltage", "Current", "Data", "SmallData", "Throughput", "Level", "Energy", "Factor", "Frequency", "Flow", "Noise", "Humidity", "TimeSpan", "Timing"];
const TICON = { Temperature: "thermometer", Fan: "fan", Control: "sliders-horizontal", Power: "zap", Load: "gauge", Clock: "activity", Voltage: "plug", Current: "cable",
  Data: "database", SmallData: "database", Throughput: "arrow-up-down", Level: "battery", Energy: "battery-charging", Factor: "layers", Frequency: "audio-waveform",
  Flow: "droplet", Noise: "volume-2", Humidity: "droplet", TimeSpan: "timer", Timing: "timer" };
const UNITS = { Temperature: [1, "°C"], Fan: [0, "RPM"], Control: [0, "%"], Load: [1, "%"], Level: [1, "%"], Humidity: [0, "%"], Power: [1, "W"], Clock: [0, "MHz"],
  Voltage: [3, "V"], Current: [2, "A"], Data: [1, "GB"], SmallData: [0, "MB"], Frequency: [0, "Hz"], Energy: [0, "mWh"], Flow: [1, "L/h"], Noise: [0, "dBA"],
  TimeSpan: [0, "s"], Factor: [2, ""], Timing: [1, "ns"] };
const PERCENT = new Set(["Load", "Control", "Level", "Humidity"]);
const trank = t => { const i = TYPES.indexOf(t); return i < 0 ? 99 : i; };

// ── numbers ──────────────────────────────────────────────────────────────
const nf = {};
const num = (v, d) => (nf[d] || (nf[d] = new Intl.NumberFormat(locale, { minimumFractionDigits: d, maximumFractionDigits: d }))).format(v);
function parts(type, v) {
  if (type === "Throughput") return v >= 1048576 ? [num(v / 1048576, 1), "MB/s"] : [num(v / 1024, 1), "KB/s"];
  if (type === "Clock" && v >= 1000) return [num(v / 1000, 2), "GHz"];
  const u = UNITS[type] || [2, ""];
  return [num(v, u[0]), u[1]];
}
const fmt = (type, v) => { const [n, u] = parts(type, v); return u ? `${n} ${u}` : n; };
// a grid of cells shares one unit, written in its title: clocks all in GHz, never 4,43 next to 545
const cellUnit = type => type === "Clock" ? "GHz" : type === "Throughput" ? "MB/s" : (UNITS[type] || [0, ""])[1];
const cellNum = (type, v) => type === "Clock" ? num(v / 1000, 2) : type === "Throughput" ? num(v / 1048576, 1) : num(v, (UNITS[type] || [2])[0]);
const limit = () => state.settings.alertAt || 90;
const level = v => v >= limit() ? 2 : v >= limit() - 10 ? 1 : 0;
const bytes = b => b >= 1e12 ? `${num(b / 1e12, 1)} TB` : `${num(b / 1e9, 0)} GB`;

// ── state ────────────────────────────────────────────────────────────────
const state = { init: null, settings: { alertAt: 90, alertsOn: true, startup: false, fans: {} }, rows: [], byKey: new Map(), main: [], full: false, emergency: false,
  hist: new Map(), sig: "", sel: null, hover: null, first: true, drag: false, notice: "", fanSig: "" };
const refs = new Map();      // sensor key -> elements to update
let kpiDefs = [], fanDefs = [];

if (host) host.addEventListener("message", e => {
  const m = e.data;
  if (m.t === "init") onInit(m);
  else if (m.t === "settings") onSettings(m);
  else if (m.t === "data") onData(m);
});
send({ t: "ready" });

function onInit(m) {
  state.init = m;
  T = TEXT[m.lang] || TEXT.en;
  locale = m.lang === "it" ? "it-IT" : "en-US";
  document.documentElement.lang = m.lang;
  state.hist = new Map(Object.entries(m.history || {}).map(([k, a]) => [k, a.slice(-HISTORY)]));
  state.sig = "";
  renderStatic();
  renderHero();
  renderNotices(true);
}

function onSettings(m) {
  const fansChanged = JSON.stringify(m.fans) !== JSON.stringify(state.settings.fans);
  state.settings = m;
  renderDrawer();
  if (fansChanged && !state.drag && !document.activeElement.closest(".fan")) renderFans();
  $("fanNote").innerHTML = state.init ? icon("shield-check") + `<span>${esc(T.fanNote(state.init.minPct, limit()))}</span>` : "";
}

function onData(m) {
  state.full = m.full;
  state.emergency = m.emergency;
  state.rows = m.rows;
  state.main = m.main;
  state.byKey = new Map(m.rows.map(r => [r.k, r]));
  for (const r of m.rows) {
    let h = state.hist.get(r.k);
    if (!h) state.hist.set(r.k, h = []);
    h.push(r.v);
    if (h.length > HISTORY) h.shift();
  }
  const sig = m.rows.map(r => r.k).join("|");
  if (sig !== state.sig) { state.sig = sig; renderLayout(); }
  update();
  renderNotices();
}

// ── static parts ─────────────────────────────────────────────────────────
function renderStatic() {
  $("logo").innerHTML = icon("activity");
  $("version").textContent = "v" + state.init.version;
  $("openSettings").innerHTML = icon("settings");
  $("openSettings").setAttribute("aria-label", T.settings);
  $("fanHead").innerHTML = secHead("fan", T.fanKicker, T.fanTitle, T.fanSide);
  $("hwHead").innerHTML = secHead("layers", T.hwKicker, T.hwTitle, T.hwSide);
  $("footer").innerHTML = `<span>${esc(T.footer(state.init.version))}</span>`;
  renderDrawer();
}

const secHead = (ic, kicker, title, side) =>
  `<span class="icon-tile">${icon(ic)}</span><div><p class="kicker">${esc(kicker)}</p><h2>${title}</h2></div><p class="side">${esc(side)}</p>`;

function renderHero() {
  const pc = state.init.pc || {};
  const cpu = pc.cpu || [], gpu = pc.gpu || [];
  $("kicker").textContent = T.kicker;
  $("title").innerHTML = esc(cpu.join(" + ")) + (gpu.length ? `<span class="serif grad">${esc(gpu.join(" + "))}</span>` : "");
  const chips = [];
  const ram = pc.ram;
  if (ram) {
    const sticks = ram.sticks || [];
    const total = sticks.reduce((a, b) => a + b, 0);
    chips.push([`memory-stick`, `${total} GB ${ram.type || ""}${ram.speed ? " · " + ram.speed + " MT/s" : ""}`]);
    const same = sticks.every(s => s === sticks[0]);
    chips.push(["layers", (same ? `${sticks.length} × ${sticks[0]} GB` : sticks.join(" + ") + " GB") + (ram.slots >= sticks.length ? " · " + T.slots(sticks.length, ram.slots) : "")]);
  }
  if (pc.board) chips.push(["circuit-board", pc.board]);
  const disks = pc.disks || [];
  if (disks.length) chips.push(["hard-drive", `${T.disks(disks.length)} · ${bytes(disks.reduce((a, d) => a + d.bytes, 0))}`, disks.map(d => `${d.name} (${bytes(d.bytes)})`).join("\n")]);
  $("pc").innerHTML = chips.map(([ic, text, title]) => `<span class="chip"${title ? ` title="${esc(title)}"` : ""}>${icon(ic)}${esc(text)}</span>`).join("");
}

function renderNotices(force) {
  if (!state.init) return;
  const i = state.init;
  const key = [i.admin, i.driver, state.emergency, limit()].join();
  const pill = $("mode");
  const mode = state.emergency ? "hot" : !i.admin ? "base" : !i.driver ? "nodriver" : "full";
  pill.className = "pill" + (mode === "hot" ? " hot" : mode === "full" ? "" : " warn");
  pill.innerHTML = `<span class="dot"></span>${esc(T.mode[mode])}`;
  if (key === state.notice && !force) return;
  state.notice = key;
  let html = "";
  if (state.emergency) {
    const [b, p] = T.hot(limit());
    html += notice("hot", "thermometer-sun", b, p, "");
  }
  if (!i.admin) html += notice("", "shield-exclamation-point", T.base[0], T.base[1], `<button class="btn btn-primary" data-send="admin">${icon("shield-check")}${esc(T.base[2])}</button>`);
  else if (!i.driver) html += notice("", "shield-exclamation-point", T.nodriver[0], T.nodriver[1],
    `<button class="btn btn-primary" data-send="pawnio">${icon("external-link")}${esc(T.nodriver[2])}</button><button class="btn btn-ghost" data-send="restart">${icon("rotate-ccw")}${esc(T.nodriver[3])}</button>`);
  $("notices").innerHTML = html;
}

const notice = (cls, ic, title, text, actions) =>
  `<div class="notice ${cls}"><span class="icon-tile">${icon(ic)}</span><div class="text"><b>${esc(title)}</b><p>${esc(text)}</p></div><div class="actions">${actions}</div></div>`;

// ── layout: rebuilt only when the set of sensors changes ──────────────────
function renderLayout() {
  renderKpis();
  renderFans();
  renderHw();
  refs.clear();
  document.querySelectorAll("[data-k]").forEach(el => {
    const k = el.dataset.k;
    const r = refs.get(k) || {};
    if (el.classList.contains("row")) { r.v = el.querySelector(".v"); r.bar = el.querySelector(".bar"); }
    if (el.classList.contains("cell")) { r.cell = el; r.cv = el.querySelector(".v"); r.set = el.parentElement; }
    refs.set(k, r);
  });
  if (state.sel && state.byKey.has(state.sel)) openDetail(state.sel); else state.sel = null;
  if (state.first) {
    state.first = false;
    const els = document.querySelectorAll(".reveal");
    els.forEach((el, n) => el.style.setProperty("--d", Math.min(n, 14) * 55 + "ms"));
    requestAnimationFrame(() => requestAnimationFrame(() => els.forEach(el => el.classList.add("in"))));
  } else document.querySelectorAll(".reveal").forEach(el => el.classList.add("in"));
}

const find = (gk, type, test) => state.rows.find(r => r.gk === gk && r.type === type && (!test || test(r.n)));

function renderKpis() {
  const rows = state.rows, main = new Set(state.main);
  kpiDefs = [];
  const cpu = rows.find(r => r.kind === "Cpu");
  if (cpu) {
    const gk = cpu.gk;
    const temp = rows.find(r => r.gk === gk && main.has(r.k));
    const load = find(gk, "Load", n => n === "CPU Total");
    const power = find(gk, "Power", n => /package/i.test(n));
    const clock = find(gk, "Clock", n => /average/i.test(n) && !/effective/i.test(n));
    kpiDefs.push({ ic: "cpu", label: T.kpi.cpu, sub: cpu.g, big: temp || load, facts: [temp && load && ["gauge", load], power && ["zap", power], clock && ["activity", clock]],
      hint: !temp && T.kpi.needAdmin });
  }
  const gmain = rows.find(r => r.kind.startsWith("Gpu") && main.has(r.k)) || rows.find(r => r.kind.startsWith("Gpu"));
  if (gmain) {
    const gk = gmain.gk;
    const temp = rows.find(r => r.gk === gk && main.has(r.k));
    const load = find(gk, "Load", n => n === "GPU Core");
    const power = find(gk, "Power");
    const fan = find(gk, "Fan");
    const hot = find(gk, "Temperature", n => /hot ?spot/i.test(n));
    kpiDefs.push({ ic: "gpu", label: T.kpi.gpu, sub: gmain.g, big: temp || load, facts: [temp && load && ["gauge", load], power && ["zap", power], fan && ["fan", fan], hot && ["thermometer-sun", hot]] });
  }
  const mem = rows.find(r => r.kind === "Memory" && r.g === "Total Memory") || rows.find(r => r.kind === "Memory");
  if (mem) {
    const used = find(mem.gk, "Data", n => /used/i.test(n));
    const avail = find(mem.gk, "Data", n => /available/i.test(n));
    const load = find(mem.gk, "Load");
    if (used) kpiDefs.push({ ic: "memory-stick", label: T.kpi.ram, sub: T.names[mem.g] || mem.g, big: used, total: avail && [used, avail], meter: load,
      facts: [load && ["gauge", load], avail && ["database", avail, T.kpi.free]] });
  }
  const powers = [kpiDefs[0], kpiDefs[1]].map(d => d && d.facts.find(f => f && f[0] === "zap")).filter(Boolean).map(f => f[1]);
  if (powers.length) kpiDefs.push({ ic: "zap", label: T.kpi.power, sub: powers.length > 1 ? T.kpi.est : powers[0].g, sum: powers,
    facts: powers.length > 1 ? powers.map((p, i) => [i ? "gpu" : "cpu", p]) : [] });

  $("kpis").innerHTML = kpiDefs.map((d, i) => `
    <article class="kpi spot reveal" data-kpi="${i}">
      <div class="kpi-head"><span class="icon-tile">${icon(d.ic)}</span><div class="what"><p class="kicker">${esc(d.label)}</p><div class="sub">${esc(d.sub)}</div></div></div>
      <div class="big"><span data-f="n"></span><small data-f="u"></small></div>
      ${d.meter ? `<div class="meter"><i data-f="meter"></i></div>` : ""}
      <div class="facts">${d.facts.filter(Boolean).map((f, j) => `<span>${icon(f[0])}<b data-f="f${j}"></b>${f[2] ? " " + esc(f[2]) : ""}</span>`).join("")}
        ${d.hint ? `<span>${icon("shield-exclamation-point")}${esc(d.hint)}</span>` : ""}</div>
      <svg class="spark" viewBox="0 0 300 64" preserveAspectRatio="none" data-f="spark"></svg>
    </article>`).join("");
  kpiDefs.forEach(d => d.facts = d.facts.filter(Boolean));
}

// controllable fans: a duty control paired with the fan that reports its speed
function fanList() {
  const out = [];
  for (const c of state.rows.filter(r => r.ctl)) {
    const f = state.rows.find(r => r.type === "Fan" && r.gk === c.gk && r.i === c.i) || state.rows.find(r => r.type === "Fan" && r.gk === c.gk && r.n === c.n);
    if (!f && !c.kind.startsWith("Gpu")) continue;   // a header with no tachometer: nothing on it, most likely
    out.push({ c, f, gpu: c.kind.startsWith("Gpu") });
  }
  return out;
}

const tempRows = () => state.rows.filter(r => r.type === "Temperature");
function defaultSource(fan) {
  const main = state.main.map(k => state.byKey.get(k)).filter(Boolean);
  const own = main.find(r => r.gk === fan.c.gk);
  return (own || main.find(r => r.kind === "Cpu") || main[0] || tempRows()[0] || {}).k || null;
}
function planOf(fan) {
  const p = state.settings.fans[fan.c.k];
  return Object.assign({ Mode: "auto", Pct: 50, Temps: [40, 60, 75, 85], Pcts: [30, 45, 70, 100], Source: defaultSource(fan), Name: null }, p || {});
}
function sendPlan(key, p) {
  state.settings.fans[key] = p;
  send({ t: "fan", k: key, mode: p.Mode, pct: p.Pct, src: p.Source, name: p.Name, temps: p.Temps, pcts: p.Pcts });
}

function renderFans() {
  fanDefs = fanList();
  const sec = $("fanSec");
  if (!state.init) return;
  sec.hidden = !fanDefs.length && state.full;
  $("fanNote").innerHTML = icon("shield-check") + `<span>${esc(fanDefs.length ? T.fanNote(state.init.minPct, limit()) : T.fanNone)}</span>`;
  $("fans").innerHTML = fanDefs.map(fan => {
    const p = planOf(fan);
    const name = p.Name || (fan.f || fan.c).n;
    return `
    <article class="fan spot${state.first ? " reveal" : ""}" data-fan="${esc(fan.c.k)}">
      <div class="fan-head"><span class="icon-tile" data-f="spin">${icon("fan")}</span>
        <div class="what" style="min-width:0;flex:1"><input class="fan-name" value="${esc(name)}" maxlength="40" spellcheck="false" title="${esc(T.rename)}"><div class="sub">${esc(fan.c.g.split(" › ").pop())}</div></div></div>
      <div class="fan-now"><div class="big"><span data-f="rpm">${fan.f ? num(fan.f.v, 0) : ""}</span><small>${fan.f ? "RPM" : ""}</small></div><span class="chip" data-f="duty"></span></div>
      <div class="fan-state" data-f="state"></div>
      <div class="seg" role="group">${["auto", "fixed", "curve"].map((m, i) =>
        `<button data-mode="${m}" aria-pressed="${p.Mode === m}">${icon(["rotate-ccw", "hand", "spline"][i])}${esc(T.fanModes[i])}</button>`).join("")}</div>
      <div class="fan-body" data-f="body">${fanBody(fan, p)}</div>
    </article>`;
  }).join("");
  document.querySelectorAll(".fan").forEach(el => wireFan(el));
}

function fanBody(fan, p) {
  if (p.Mode === "fixed") return `<div class="range-row"><input type="range" min="${state.init.minPct}" max="100" value="${p.Pct}" data-f="pct"><output>${p.Pct} %</output></div><p>${esc(T.fanFixed)}</p>`;
  if (p.Mode === "curve") {
    const opts = tempRows().map(r => `<option value="${esc(r.k)}"${r.k === p.Source ? " selected" : ""}>${esc((T.names[r.g] || r.g.split(" › ").pop()) + " · " + r.n)}</option>`).join("");
    return `<label style="display:flex;gap:.6rem;align-items:center">${esc(T.fanSource)}<select class="field" data-f="src">${opts}</select></label><svg class="curve" viewBox="0 0 300 172" data-f="curve"></svg>`;
  }
  return `<p>${esc(T.fanAuto(fan.gpu))}</p>`;
}

function wireFan(el) {
  const key = el.dataset.fan;
  const fan = fanDefs.find(f => f.c.k === key);
  const plan = () => planOf(fan);
  el.querySelectorAll(".seg button").forEach(b => b.onclick = () => {
    const p = plan();
    p.Mode = b.dataset.mode;
    if (p.Mode === "curve" && !p.Source) p.Source = defaultSource(fan);
    sendPlan(key, p);
    el.querySelectorAll(".seg button").forEach(x => x.setAttribute("aria-pressed", x === b));
    el.querySelector("[data-f=body]").innerHTML = fanBody(fan, p);
    wireBody(el, fan, key, plan);
  });
  const name = el.querySelector(".fan-name");
  name.onkeydown = e => { if (e.key === "Enter") name.blur(); };
  name.onchange = () => { const p = plan(); p.Name = name.value.trim() || null; sendPlan(key, p); };
  wireBody(el, fan, key, plan);
}

function wireBody(el, fan, key, plan) {
  const range = el.querySelector("[data-f=pct]");
  if (range) {
    range.oninput = () => range.nextElementSibling.textContent = range.value + " %";
    range.onchange = () => { const p = plan(); p.Pct = +range.value; sendPlan(key, p); };
  }
  const src = el.querySelector("[data-f=src]");
  if (src) src.onchange = () => { const p = plan(); p.Source = src.value; sendPlan(key, p); drawCurve(el, fan); };
  const svg = el.querySelector("[data-f=curve]");
  if (svg) { drawCurve(el, fan); dragCurve(svg, fan, key, plan, el); }
}

// the curve editor: x from 20 to 100 °C, y from 0 to 100 %
const CX = t => 34 + (t - 20) / 80 * 254, CY = p => 12 + (100 - p) / 100 * 136;
const curveAt = (temps, pcts, t) => {
  if (t <= temps[0]) return pcts[0];
  for (let i = 1; i < temps.length; i++) if (t <= temps[i]) return pcts[i - 1] + (pcts[i] - pcts[i - 1]) * (t - temps[i - 1]) / (temps[i] - temps[i - 1]);
  return pcts[pcts.length - 1];
};
function drawCurve(el, fan, p) {
  const svg = el.querySelector("[data-f=curve]");
  if (!svg) return;
  p = p || planOf(fan);
  const pts = p.Temps.map((t, i) => [CX(Math.max(20, Math.min(100, t))), CY(p.Pcts[i])]);
  const line = `M${CX(20)},${pts[0][1]} L` + pts.map(q => q.join(",")).join(" L") + ` L${CX(100)},${pts[pts.length - 1][1]}`;
  const src = state.byKey.get(p.Source);
  let now = "";
  if (src) {
    const t = Math.max(20, Math.min(100, src.v)), y = CY(curveAt(p.Temps, p.Pcts, src.v));
    now = `<g class="now"><line x1="${CX(t)}" x2="${CX(t)}" y1="12" y2="148"/><circle cx="${CX(t)}" cy="${y}" r="4"/></g>`;
  }
  const grid = [20, 40, 60, 80, 100].map(t => `<line x1="${CX(t)}" x2="${CX(t)}" y1="12" y2="148"/><text x="${CX(t)}" y="164" text-anchor="middle">${t}°</text>`).join("")
    + [0, 50, 100].map(v => `<line x1="34" x2="288" y1="${CY(v)}" y2="${CY(v)}"/><text x="28" y="${CY(v) + 3}" text-anchor="end">${v}%</text>`).join("");
  svg.innerHTML = `<defs><linearGradient id="cg-${esc(fan.c.k).replace(/\W/g, "")}" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#2fe37f" stop-opacity=".28"/><stop offset="1" stop-color="#2fe37f" stop-opacity="0"/></linearGradient></defs>
    <g class="grid">${grid}</g>
    <path d="${line} L${CX(100)},148 L${CX(20)},148 Z" fill="url(#cg-${esc(fan.c.k).replace(/\W/g, "")})"/>
    <path d="${line}" fill="none" stroke="#2fe37f" stroke-width="2" stroke-linejoin="round"/>
    ${now}
    ${pts.map((q, i) => `<circle class="handle" data-i="${i}" cx="${q[0]}" cy="${q[1]}" r="5.5"><title>${p.Temps[i]} °C → ${p.Pcts[i]} %</title></circle>`).join("")}`;
}
function dragCurve(svg, fan, key, plan, el) {
  let i = -1, p = null;
  svg.onpointerdown = e => {
    if (!e.target.classList.contains("handle")) return;
    i = +e.target.dataset.i; p = plan(); p.Temps = p.Temps.slice(); p.Pcts = p.Pcts.slice();
    state.drag = true;
    svg.setPointerCapture(e.pointerId);
  };
  svg.onpointermove = e => {
    if (i < 0) return;
    const pt = new DOMPoint(e.clientX, e.clientY).matrixTransform(svg.getScreenCTM().inverse());
    const lo = i ? p.Temps[i - 1] + 1 : 20, hi = i < p.Temps.length - 1 ? p.Temps[i + 1] - 1 : 100;
    p.Temps[i] = Math.round(Math.max(lo, Math.min(hi, 20 + (pt.x - 34) / 254 * 80)));
    p.Pcts[i] = Math.round(Math.max(state.init.minPct, Math.min(100, 100 - (pt.y - 12) / 136 * 100)));
    drawCurve(el, fan, p);
    const h = svg.querySelector(`.handle[data-i="${i}"]`);
    if (h) h.classList.add("drag");
  };
  svg.onpointerup = svg.onpointercancel = () => {
    if (i < 0) return;
    i = -1; state.drag = false;
    sendPlan(key, p);
  };
}

function renderHw() {
  const map = new Map();
  for (const r of state.rows) {
    let g = map.get(r.gk);
    if (!g) map.set(r.gk, g = { gk: r.gk, name: r.g, kind: r.kind, rank: r.rank, rows: [] });
    g.rows.push(r);
  }
  const groups = [...map.values()].sort((a, b) => a.rank - b.rank || a.name.localeCompare(b.name) || a.gk.localeCompare(b.gk));
  $("hw").innerHTML = groups.map(card).join("");
}

function card(g) {
  const [ic, kind] = KIND[g.kind] || ["box", "other"];
  const names = g.name.split(" › ");
  const title = T.names[names[names.length - 1]] || names[names.length - 1];
  const sub = names.length > 1 ? names[0] : T.kinds[kind];
  const byType = new Map();
  for (const r of g.rows) { if (!byType.has(r.type)) byType.set(r.type, []); byType.get(r.type).push(r); }
  const types = [...byType.keys()].sort((a, b) => trank(a) - trank(b));
  const hasTemp = byType.has("Temperature");
  return `<article class="card spot${state.first ? " reveal" : ""}${g.rows.length > 28 ? " wide" : ""}" data-gk="${esc(g.gk)}">
    <div class="card-head"><span class="icon-tile sm">${icon(g.kind === "Network" && /wi-?fi|wireless/i.test(title) ? "wifi" : ic)}</span>
      <div class="what"><h3 title="${esc(title)}">${esc(title)}</h3><div class="sub">${esc(sub)}</div></div>
      ${hasTemp ? `<span class="chip" data-hot="${esc(g.gk)}">${icon("thermometer")}<span></span></span>` : ""}</div>
    <div class="types">${types.map(t => typeBlock(t, byType.get(t).sort((a, b) => a.i - b.i), g.gk)).join("")}</div>
  </article>`;
}

// sensors that differ only by their number (Core #1, Core #2...) become a grid of cells
// sections fold away on a click; RAM timings and multipliers never change, so they start folded
const SHUT = new Set(["Timing", "Factor"]);
const fold = new Map();   // "gk|type" -> folded, kept across layouts
function typeBlock(type, rows, gk) {
  const fk = gk + "|" + type;
  const shut = fold.has(fk) ? fold.get(fk) : SHUT.has(type);
  const sets = new Map(), plain = [];
  for (const r of rows) {
    const m = /#\d+/.exec(r.n);
    if (!m) { plain.push(r); continue; }
    const pat = r.n.replace(/#\d+/, "#");
    if (!sets.has(pat)) sets.set(pat, []);
    sets.get(pat).push(r);
  }
  for (const [pat, rs] of [...sets]) if (rs.length < 4) { plain.push(...rs); sets.delete(pat); }
  plain.sort((a, b) => a.i - b.i);
  const rowHtml = r => `<div class="row" data-k="${esc(r.k)}"><span class="n" title="${esc(r.n)}">${esc(r.n)}</span>${PERCENT.has(type) || type === "Temperature" ? `<span class="bar"><i></i></span>` : "<span></span>"}<span class="v"></span></div>`;
  const unit = cellUnit(type);
  const cells = [...sets].map(([pat, rs]) => `<div class="cells-title">${esc(pat.replace(/\s*#\s*/, " ").trim())}${unit ? " · " + unit : ""}</div><div class="cells">${rs.map(r =>
    `<div class="cell" data-k="${esc(r.k)}" title="${esc(r.n)}"><span class="n">#${/#(\d+)/.exec(r.n)[1]}</span><span class="v"></span></div>`).join("")}</div>`).join("");
  return `<div class="type${shut ? " shut" : ""}"><button class="type-head" data-fold="${esc(fk)}" aria-expanded="${!shut}">${icon(TICON[type] || "layers")}${esc(T.types[type] || type)}<span class="count">${rows.length}</span>${icon("chevron-down", "chev")}</button>
    <div class="type-body">${plain.map(rowHtml).join("")}${cells}</div></div>`;
}

// ── every second ─────────────────────────────────────────────────────────
function update() {
  const setMax = new Map();
  for (const r of state.rows) {
    const el = refs.get(r.k);
    if (!el) continue;
    if (el.v) el.v.textContent = fmt(r.type, r.v);
    if (el.bar) {
      const pct = r.type === "Temperature" ? r.v / limit() * 100 : r.v;
      el.bar.firstChild.style.width = Math.max(0, Math.min(100, pct)) + "%";
      el.bar.className = "bar" + (r.type === "Temperature" ? " lvl-" + level(r.v) : "");
    }
    if (el.cell) {
      el.cv.textContent = cellNum(r.type, r.v);
      if (!PERCENT.has(r.type)) setMax.set(el.set, Math.max(setMax.get(el.set) || 0, r.max || r.v));
    }
  }
  for (const r of state.rows) {
    const el = refs.get(r.k);
    if (!el || !el.cell) continue;
    const h = PERCENT.has(r.type) ? r.v / 100 : r.type === "Temperature" ? (r.v - 25) / (limit() - 25) : r.v / (setMax.get(el.set) || 1);
    el.cell.style.setProperty("--h", Math.max(0.04, Math.min(1, h)).toFixed(3));
  }
  document.querySelectorAll("[data-hot]").forEach(chip => {
    const temps = state.rows.filter(r => r.gk === chip.dataset.hot && r.type === "Temperature").map(r => r.v);
    if (!temps.length) return;
    const t = Math.max(...temps);
    chip.lastChild.textContent = fmt("Temperature", t);
    chip.className = "chip lvl-" + level(t);
  });
  updateKpis();
  updateFans();
  if (state.sel) drawDetail();
}

function updateKpis() {
  kpiDefs.forEach((d, i) => {
    const el = document.querySelector(`[data-kpi="${i}"]`);
    if (!el) return;
    const q = f => el.querySelector(`[data-f="${f}"]`);
    let big, type, series;
    if (d.sum) {
      const vals = d.sum.map(r => state.byKey.get(r.k)).filter(Boolean);
      big = vals.reduce((a, r) => a + r.v, 0); type = "Power";
      const hs = d.sum.map(r => state.hist.get(r.k) || []);
      const n = Math.min(...hs.map(h => h.length));
      series = Array.from({ length: n }, (_, j) => hs.reduce((a, h) => a + h[h.length - n + j], 0));
    } else {
      const r = d.big && state.byKey.get(d.big.k);
      if (!r) return;
      big = r.v; type = r.type; series = state.hist.get(r.k) || [];
    }
    const [n, u] = parts(type, big);
    q("n").textContent = n;
    q("u").textContent = d.total ? `${u} / ${num(d.total.reduce((a, r) => a + (state.byKey.get(r.k) || r).v, 0), 0)} GB` : u;
    el.querySelector(".big").className = "big" + (type === "Temperature" ? " lvl-" + level(big) : "");
    if (d.meter) { const m = state.byKey.get(d.meter.k); if (m) q("meter").style.width = m.v + "%"; }
    d.facts.forEach((f, j) => { const r = state.byKey.get(f[1].k); if (r) q("f" + j).textContent = fmt(r.type, r.v); });
    q("spark").innerHTML = spark(series, "k" + i);
  });
}

function updateFans() {
  fanDefs.forEach(fan => {
    const el = document.querySelector(`.fan[data-fan="${CSS.escape(fan.c.k)}"]`);
    if (!el) return;
    const c = state.byKey.get(fan.c.k), f = fan.f && state.byKey.get(fan.f.k);
    const p = planOf(fan);
    const q = s => el.querySelector(`[data-f="${s}"]`);
    if (f) {
      q("rpm").textContent = num(f.v, 0);
      const spin = q("spin");
      spin.style.setProperty("--spin", Math.min(4, Math.max(0.35, 3000 / Math.max(f.v, 1))) + "s");
      spin.style.setProperty("--play", f.v > 50 ? "running" : "paused");
    }
    if (c) q("duty").textContent = fmt("Control", c.v);
    const st = q("state");
    const safety = state.emergency && p.Mode !== "auto";
    st.textContent = safety ? T.fanSafety : c && c.mode === "Software" ? T.fanByVigil : T.fanByBios(fan.gpu);
    st.className = "fan-state" + (safety ? " hot" : "");
    if (p.Mode === "curve" && !state.drag) drawCurve(el, fan);
  });
}

// ── charts ───────────────────────────────────────────────────────────────
function spark(data, id) {
  if (data.length < 2) return "";
  let lo = Math.min(...data), hi = Math.max(...data);
  const pad = Math.max((hi - lo) * 0.25, Math.abs(hi) * 0.02 + 0.5);
  lo -= pad; hi += pad;
  const x = i => 300 - (data.length - 1 - i) * (300 / (HISTORY - 1)), y = v => 60 - (v - lo) / (hi - lo) * 52;
  const line = data.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join("");
  return `<defs><linearGradient id="${id}" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#2fe37f" stop-opacity=".3"/><stop offset="1" stop-color="#2fe37f" stop-opacity="0"/></linearGradient></defs>
    <path d="${line}L300,64L${x(0).toFixed(1)},64Z" fill="url(#${id})"/><path d="${line}" fill="none" stroke="#2fe37f" stroke-width="1.6" vector-effect="non-scaling-stroke"/>`;
}

// the last ten minutes of one sensor, opened under the row (or the grid) you clicked
document.addEventListener("click", e => {
  const t = e.target.closest("[data-k], [data-send], [data-open], [data-close], [data-fold], #openSettings, #scrim");
  if (!t) return;
  if (t.dataset.fold) {
    const box = t.parentElement, shut = !box.classList.contains("shut");
    box.classList.toggle("shut", shut);
    t.setAttribute("aria-expanded", !shut);
    fold.set(t.dataset.fold, shut);
    if (shut && box.querySelector(".detail")) { closeDetail(); state.sel = null; }
    return;
  }
  if (t.dataset.send) return send({ t: t.dataset.send });
  if (t.dataset.open) return send({ t: "open", url: t.dataset.open });
  if (t.id === "openSettings") return document.body.classList.add("open");
  if (t.id === "scrim" || t.dataset.close !== undefined) return document.body.classList.remove("open");
  const k = t.dataset.k;
  if (state.sel === k) { closeDetail(); state.sel = null; return; }
  openDetail(k);
});
document.addEventListener("keydown", e => { if (e.key === "Escape") document.body.classList.remove("open"); });
document.addEventListener("pointermove", e => {
  const el = e.target.closest && e.target.closest(".spot");
  if (!el) return;
  const b = el.getBoundingClientRect();
  el.style.setProperty("--x", e.clientX - b.left + "px");
  el.style.setProperty("--y", e.clientY - b.top + "px");
});

function closeDetail() {
  document.querySelectorAll(".detail").forEach(d => d.remove());
  document.querySelectorAll(".sel").forEach(d => d.classList.remove("sel"));
}
function openDetail(k) {
  closeDetail();
  const el = document.querySelector(`[data-k="${CSS.escape(k)}"]`);
  if (!el) return;
  state.sel = k; state.hover = null;
  el.classList.add("sel");
  const d = document.createElement("div");
  d.className = "detail";
  d.innerHTML = `<div class="detail-head"></div><svg class="chart"></svg>`;
  (el.classList.contains("cell") ? el.parentElement : el).insertAdjacentElement("afterend", d);
  const svg = d.querySelector("svg");
  svg.onpointermove = e => { const b = svg.getBoundingClientRect(); state.hover = (e.clientX - b.left) / b.width; drawDetail(); };
  svg.onpointerleave = () => { state.hover = null; drawDetail(); };
  drawDetail();
}
function drawDetail() {
  const d = document.querySelector(".detail"), r = state.byKey.get(state.sel);
  if (!d || !r) return;
  const data = state.hist.get(r.k) || [];
  const svg = d.querySelector("svg");
  const w = Math.max(200, svg.clientWidth), h = 150, L = 58, R = 8, top = 8, bottom = 22;
  const avg = data.reduce((a, b) => a + b, 0) / (data.length || 1);
  let head = `<span>${esc(r.n)}</span><span>${T.now} <b>${fmt(r.type, r.v)}</b></span>`
    + (r.min != null ? `<span>${T.min} <b>${fmt(r.type, r.min)}</b></span>` : "")
    + `<span>${T.avg} <b>${fmt(r.type, avg)}</b></span>`
    + (r.max != null ? `<span>${T.max} <b>${fmt(r.type, r.max)}</b></span>` : "");
  if (data.length < 2) { d.querySelector(".detail-head").innerHTML = head; svg.innerHTML = ""; return; }
  let lo = Math.min(...data), hi = Math.max(...data);
  const pad = Math.max((hi - lo) * 0.15, Math.abs(hi) * 0.02 + 0.5);
  lo -= pad; hi += pad;
  if (lo < 0 && Math.min(...data) >= 0) lo = 0;
  const pw = w - L - R, ph = h - top - bottom;
  const x = i => L + pw - (data.length - 1 - i) * (pw / (HISTORY - 1)), y = v => top + ph - (v - lo) / (hi - lo) * ph;
  const line = data.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join("");
  const grid = [0, 0.5, 1].map(f => { const v = hi - (hi - lo) * f, yy = top + ph * f;
    return `<line x1="${L}" x2="${w - R}" y1="${yy}" y2="${yy}"/><text x="${L - 8}" y="${yy + 3}" text-anchor="end">${fmt(r.type, v)}</text>`; }).join("");
  let cross = "";
  if (state.hover != null) {
    const i = Math.round((data.length - 1) - (1 - (state.hover * w - L) / pw) * (HISTORY - 1));
    if (i >= 0 && i < data.length) {
      cross = `<g class="cross"><line x1="${x(i)}" x2="${x(i)}" y1="${top}" y2="${top + ph}"/><circle cx="${x(i)}" cy="${y(data[i])}" r="4"/></g>`;
      head = `<span>${esc(r.n)}</span><span><b>${fmt(r.type, data[i])}</b> · ${T.secAgo(data.length - 1 - i)}</span>`;
    }
  }
  d.querySelector(".detail-head").innerHTML = head;
  svg.setAttribute("viewBox", `0 0 ${w} ${h}`);
  svg.innerHTML = `<defs><linearGradient id="dg" x1="0" x2="0" y1="0" y2="1"><stop offset="0" stop-color="#2fe37f" stop-opacity=".32"/><stop offset="1" stop-color="#2fe37f" stop-opacity="0"/></linearGradient></defs>
    <g class="grid">${grid}</g>
    <path d="${line}L${x(data.length - 1)},${top + ph}L${x(0)},${top + ph}Z" fill="url(#dg)"/>
    <path d="${line}" fill="none" stroke="#2fe37f" stroke-width="2" stroke-linejoin="round"/>
    ${cross}
    <text x="${L}" y="${h - 4}">${T.ago10}</text><text x="${w - R}" y="${h - 4}" text-anchor="end">${T.now}</text>`;
}

// ── settings ─────────────────────────────────────────────────────────────
function renderDrawer() {
  if (!state.init) return;
  const s = state.settings, i = state.init;
  const startupText = i.portable ? T.startupPortable : i.admin ? T.startupAdmin : T.startupBase;
  $("drawer").innerHTML = `
    <div class="drawer-head"><h2>${esc(T.settings)}</h2><button class="icon-btn" data-close aria-label="close">${icon("x")}</button></div>
    <div class="setting"><label class="setting-row"><span class="label">${esc(T.startup)}</span><input type="checkbox" class="switch" id="sStartup"${s.startup ? " checked" : ""}${i.portable ? " disabled" : ""}></label><p>${esc(startupText)}</p></div>
    <div class="setting"><label class="setting-row"><span class="label">${esc(T.alerts)}</span><input type="checkbox" class="switch" id="sAlerts"${s.alertsOn ? " checked" : ""}></label>
      <div class="setting-row" style="margin-top:.8rem"><span class="label" style="font-weight:400;color:var(--muted)">${esc(T.limit)}</span><span class="num"><input type="number" id="sLimit" min="50" max="110" value="${s.alertAt}"> °C</span></div>
      <p>${esc(T.alertsText)}</p></div>
    <div class="setting"><p class="about">${esc(T.about(i.version))}</p>
      <div class="links"><button class="btn btn-ghost" data-open="https://github.com/davide-mariotti/Vigil">${icon("code-xml")}${esc(T.links[0])}</button><button class="btn btn-ghost" data-open="https://ko-fi.com/Z0X32ATNHL">${icon("coffee")}${esc(T.links[1])}</button></div></div>`;
  $("sStartup").onchange = e => send({ t: "set", startup: e.target.checked });
  $("sAlerts").onchange = e => send({ t: "set", alertsOn: e.target.checked });
  $("sLimit").onchange = e => send({ t: "set", alertAt: Math.max(50, Math.min(110, +e.target.value || 90)) });
}
