// Lumi site: what both pages share. The pixel ghost, RU/EN, scroll reveals, the mouse light on cards, live GitHub numbers.
// A page sets window.PAGE = { en: {...}, onLang(lang) {...} } before this script.
(function () {
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const PAGE = window.PAGE || {};

  // ---------- the ghost: the same 14×13 grid as the apps. Eyes can look around (dx, dy in pixels).
  const BODY = ["....BBBBBB....", "..BBBBBBBBBB..", ".BBBBBBBBBBBB.", ".BBBBBBBBBBBB.", "BBBBBBBBBBBBBB", "BBBBBBBBBBBBBB",
                "BBBBBBBBBBBBBB", "BBBBBBBBBBBBBB", "BBBBBBBBBBBBBB", "BBBBBBBBBBBBBB"];
  const HEMS = ["BB..BBBBBB..BB", "..BBBB..BBBB.."];
  function drawGhost(canvas, step, dx = 0, dy = 0) {
    const c = canvas.getContext("2d");
    const body = canvas.dataset.color || "#5fd3ad", eye = "#0b1514";
    c.clearRect(0, 0, 14, 13);
    const lift = step ? 0 : 1;
    [...BODY, HEMS[step]].forEach((row, y) => [...row].forEach((ch, x) => {
      if (ch === ".") return;
      c.fillStyle = body; c.fillRect(x, y + lift, 1, 1);
    }));
    c.fillStyle = eye;
    c.fillRect(3 + dx, 4 + dy + lift, 2, 2); c.fillRect(9 + dx, 4 + dy + lift, 2, 2);
    c.fillRect(6, 7 + lift, 2, 1);
  }
  const ghosts = [...document.querySelectorAll("canvas.ghost")];
  let gstep = 0, look = [0, 0];
  const paint = () => ghosts.forEach(g => drawGhost(g, g.classList.contains("live") ? gstep : 0, ...(g.classList.contains("looks") ? look : [0, 0])));
  paint();
  if (!reduced) setInterval(() => { gstep = 1 - gstep; paint(); }, 480);
  // Eyes follow the pointer, one pixel at most.
  addEventListener("pointermove", e => {
    const g = document.querySelector("canvas.looks");
    if (!g) return;
    const r = g.getBoundingClientRect();
    const x = e.clientX - (r.left + r.width / 2), y = e.clientY - (r.top + r.height / 2);
    const nx = Math.abs(x) > r.width * .6 ? Math.sign(x) : 0, ny = Math.abs(y) > r.height * .6 ? Math.sign(y) : 0;
    if (nx !== look[0] || ny !== look[1]) { look = [nx, ny]; paint(); }
  }, { passive: true });

  // ---------- floating pixels, like the Lumi OS wallpaper
  function pixels(canvas, count = 70) {
    const c = canvas.getContext("2d");
    let w, h, dots;
    const size = () => {
      const dpr = Math.min(2, devicePixelRatio || 1);
      w = canvas.clientWidth; h = canvas.clientHeight;
      canvas.width = w * dpr; canvas.height = h * dpr; c.setTransform(dpr, 0, 0, dpr, 0, 0);
      dots = Array.from({ length: count }, () => ({ x: Math.random() * w, y: Math.random() * h, s: 2 + Math.random() * 6,
        v: .08 + Math.random() * .3, a: .15 + Math.random() * .5 }));
    };
    size(); addEventListener("resize", size);
    let on = true;
    new IntersectionObserver(([e]) => { on = e.isIntersecting; }).observe(canvas);
    const tick = () => {
      if (on) {
        c.clearRect(0, 0, w, h);
        for (const d of dots) {
          if (!reduced) { d.y -= d.v; if (d.y < -10) { d.y = h + 10; d.x = Math.random() * w; } }
          c.fillStyle = `rgba(95,211,173,${d.a})`; c.fillRect(Math.round(d.x), Math.round(d.y), d.s, d.s);
        }
      }
      requestAnimationFrame(tick);
    };
    tick();
  }
  document.querySelectorAll("canvas.pixels").forEach(c => pixels(c, +c.dataset.count || 70));

  // ---------- RU from the page itself, EN from PAGE.en
  const RU = {};
  document.querySelectorAll("[data-i18n]").forEach(e => RU[e.dataset.i18n] = e.textContent);
  document.querySelectorAll("[data-i18n-html]").forEach(e => RU[e.dataset.i18nHtml] = e.innerHTML);
  let lang = "ru";
  try { lang = localStorage.getItem("lang") || ((navigator.language || "ru").startsWith("ru") ? "ru" : "en"); } catch (e) {}
  if (lang !== "ru") lang = "en";
  const t = key => (lang === "ru" ? RU : PAGE.en || {})[key] ?? RU[key];

  const ua = navigator.userAgent;
  const os = /Mac/.test(ua) && !/iPhone|iPad/.test(ua) ? "mac" : /Win/.test(ua) ? "win" : /Linux|X11/.test(ua) && !/Android/.test(ua) ? "linux" : null;

  function applyLang() {
    const dict = lang === "ru" ? RU : PAGE.en || {};
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-i18n]").forEach(e => { if (dict[e.dataset.i18n] != null) e.textContent = dict[e.dataset.i18n]; });
    document.querySelectorAll("[data-i18n-html]").forEach(e => { if (dict[e.dataset.i18nHtml] != null) e.innerHTML = dict[e.dataset.i18nHtml]; });
    const b = document.getElementById("lang");
    if (b) b.textContent = lang === "ru" ? "EN" : "RU";
    if (dict.title) document.title = dict.title;
    renderStats();
    PAGE.onLang && PAGE.onLang(lang);
  }
  const lb = document.getElementById("lang");
  if (lb) lb.onclick = () => { lang = lang === "ru" ? "en" : "ru"; try { localStorage.setItem("lang", lang); } catch (e) {} applyLang(); };

  // ---------- nav gets solid once the page scrolls
  const nav = document.querySelector(".nav");
  const onScroll = () => nav && nav.classList.toggle("solid", scrollY > 30);
  addEventListener("scroll", onScroll, { passive: true }); onScroll();

  // ---------- reveals
  const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } }),
    { threshold: 0.15, rootMargin: "0px 0px -40px 0px" });
  document.querySelectorAll(".reveal").forEach(e => io.observe(e));

  // ---------- light under the pointer on cards
  document.addEventListener("pointermove", e => {
    const card = e.target.closest && e.target.closest(".card");
    if (!card) return;
    const r = card.getBoundingClientRect();
    card.style.setProperty("--mx", e.clientX - r.left + "px"); card.style.setProperty("--my", e.clientY - r.top + "px");
  }, { passive: true });

  // ---------- scroll progress of a tall section with a sticky inside: 0 when it reaches the top, 1 when it leaves
  function progress(el) {
    const r = el.getBoundingClientRect();
    return Math.min(1, Math.max(0, -r.top / Math.max(1, r.height - innerHeight)));
  }
  // and of any element passing through the viewport: 0 as it enters at the bottom, 1 when its centre is in the middle
  function entering(el) {
    const r = el.getBoundingClientRect();
    return Math.min(1, Math.max(0, (innerHeight - r.top) / (innerHeight / 2 + r.height / 2)));
  }
  const scrollFns = [];
  let queued = false;
  const runScroll = () => { queued = false; scrollFns.forEach(f => f()); };
  addEventListener("scroll", () => { if (!queued) { queued = true; requestAnimationFrame(runScroll); } }, { passive: true });
  addEventListener("resize", runScroll);

  // ---------- live numbers from GitHub
  const counts = {};
  const plural = (n, one, few, many) => { const a = n % 10, b = n % 100; return a === 1 && b !== 11 ? one : a >= 2 && a <= 4 && (b < 12 || b > 14) ? few : many; };
  function renderStats() {
    document.querySelectorAll("[data-count]").forEach(el => {
      const n = counts[el.dataset.count];
      if (n == null) return;
      const box = el.closest("[data-stat]") || el;
      el.textContent = n.toLocaleString(lang === "ru" ? "ru-RU" : "en-US");
      const unit = box.querySelector("[data-unit]");
      if (unit) unit.textContent = el.dataset.count === "stars"
        ? (lang === "ru" ? plural(n, "звезда на GitHub", "звезды на GitHub", "звёзд на GitHub") : n === 1 ? "star on GitHub" : "stars on GitHub")
        : (lang === "ru" ? plural(n, "скачивание", "скачивания", "скачиваний") : n === 1 ? "download" : "downloads");
      box.hidden = false;
    });
    PAGE.onCounts && PAGE.onCounts(counts, lang);
  }
  // GitHub lets a visitor's address make 60 unsigned calls an hour, so answers are kept for an hour, and the last known
  // ones are shown when GitHub says no.
  const gh = "https://api.github.com/repos/AlbertS15/Lumi";
  const HOUR = 3600e3;
  function cached(key, url, pick) {
    let old = null;
    try { old = JSON.parse(localStorage.getItem("gh:" + key)); } catch (e) {}
    if (old && Date.now() - old.t < HOUR) return Promise.resolve(old.v);
    return fetch(url).then(r => r.ok ? r.json() : Promise.reject(r.status)).then(json => {
      const v = pick(json);
      try { localStorage.setItem("gh:" + key, JSON.stringify({ t: Date.now(), v })); } catch (e) {}
      return v;
    }).catch(() => old ? old.v : null);
  }
  cached("stars", gh, repo => repo.stargazers_count).then(n => { if (n != null) { counts.stars = n; renderStats(); } });
  if (document.querySelector('[data-count="downloads"]'))
    cached("downloads", gh + "/releases?per_page=100", list => list.reduce((s, rel) => s + rel.assets.reduce((x, a) => x + a.download_count, 0), 0))
      .then(n => { if (n != null) { counts.downloads = n; renderStats(); } });

  // ---------- copy buttons
  document.querySelectorAll("[data-copy]").forEach(b => b.addEventListener("click", () => {
    const text = document.querySelector(b.dataset.copy).innerText.trim();
    navigator.clipboard && navigator.clipboard.writeText(text).then(() => {
      const old = b.textContent; b.textContent = lang === "ru" ? "Скопировано" : "Copied";
      setTimeout(() => b.textContent = old, 1400);
    });
  }));

  // The page calls Site.applyLang() once its own script has set up what onLang needs.
  window.Site = { reduced, os, get lang() { return lang; }, t, applyLang, progress, entering, onScroll: f => { scrollFns.push(f); f(); }, drawGhost, cached };
})();
