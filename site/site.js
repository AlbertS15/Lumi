// Lumi site: what every page shares. The pixel ghost, the language menu (RU, EN, DE, ES, FR), scroll reveals,
// the mouse light on cards, live GitHub numbers.
// A page sets window.PAGE = { en: {...}, de: {...}, es: {...}, fr: {...}, onLang(lang) {...} } before this script.
// Russian is the page's own text; a missing key in DE/ES/FR falls back to English, then to Russian.
(function () {
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const PAGE = window.PAGE || {};
  // Weak phones and laptops on battery saver get fewer floating pixels, drawn at 1x and at 30 fps.
  const conn = navigator.connection || {};
  const lowPower = matchMedia("(max-width: 700px)").matches || (navigator.hardwareConcurrency || 8) <= 4 ||
    (navigator.deviceMemory || 8) <= 4 || !!conn.saveData;

  // ---------- languages
  const LANGS = {
    ru: { name: "Русский", locale: "ru-RU" },
    en: { name: "English", locale: "en-US" },
    de: { name: "Deutsch", locale: "de-DE" },
    es: { name: "Español", locale: "es-ES" },
    fr: { name: "Français", locale: "fr-FR" },
  };
  // Strings every page has: the skip link, the menu, the shared footer links, the units under the live numbers.
  const UI = {
    ru: { ui_skip: "Перейти к содержанию", ui_lang: "Язык", ui_about: "О проекте", ui_changelog: "Что нового", copied: "Скопировано" },
    en: { ui_skip: "Skip to content", ui_lang: "Language", ui_about: "About", ui_changelog: "Changelog", copied: "Copied" },
    de: { ui_skip: "Zum Inhalt springen", ui_lang: "Sprache", ui_about: "Über Lumi", ui_changelog: "Änderungen", copied: "Kopiert" },
    es: { ui_skip: "Ir al contenido", ui_lang: "Idioma", ui_about: "Acerca de", ui_changelog: "Novedades", copied: "Copiado" },
    fr: { ui_skip: "Aller au contenu", ui_lang: "Langue", ui_about: "À propos", ui_changelog: "Nouveautés", copied: "Copié" },
  };
  const ruPlural = (n, one, few, many) => { const a = n % 10, b = n % 100; return a === 1 && b !== 11 ? one : a >= 2 && a <= 4 && (b < 12 || b > 14) ? few : many; };
  const UNITS = {
    ru: { stars: ["звезда на GitHub", "звезды на GitHub", "звёзд на GitHub"], downloads: ["скачивание", "скачивания", "скачиваний"], releases: ["версия", "версии", "версий"] },
    en: { stars: ["star on GitHub", "stars on GitHub"], downloads: ["download", "downloads"], releases: ["release", "releases"] },
    de: { stars: ["Stern auf GitHub", "Sterne auf GitHub"], downloads: ["Download", "Downloads"], releases: ["Version", "Versionen"] },
    es: { stars: ["estrella en GitHub", "estrellas en GitHub"], downloads: ["descarga", "descargas"], releases: ["versión", "versiones"] },
    fr: { stars: ["étoile sur GitHub", "étoiles sur GitHub"], downloads: ["téléchargement", "téléchargements"], releases: ["version", "versions"] },
  };
  const unit = (kind, n) => {
    const u = UNITS[lang][kind];
    if (!u) return "";
    if (lang === "ru") return ruPlural(n, ...u);
    return new Intl.PluralRules(LANGS[lang].locale).select(n) === "one" ? u[0] : u[1];
  };

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
  // Every canvas on the site is decoration; the text around it says the same thing.
  document.querySelectorAll("canvas").forEach(c => c.setAttribute("aria-hidden", "true"));
  // Rolling button labels repeat the text for the hover effect; screen readers should hear it once.
  document.querySelectorAll(".roll > span + span").forEach(s => s.setAttribute("aria-hidden", "true"));

  const ghosts = [...document.querySelectorAll("canvas.ghost")];
  let gstep = 0, look = [0, 0];
  const paint = () => ghosts.forEach(g => drawGhost(g, g.classList.contains("live") ? gstep : 0, ...(g.classList.contains("looks") ? look : [0, 0])));
  paint();
  if (!reduced) setInterval(() => { if (!document.hidden) { gstep = 1 - gstep; paint(); } }, 480);
  // Eyes follow the pointer, one pixel at most. Read once per frame, not once per event.
  const looker = document.querySelector("canvas.looks");
  let lookEvt = null;
  if (looker) addEventListener("pointermove", e => {
    if (!lookEvt) requestAnimationFrame(() => {
      const ev = lookEvt; lookEvt = null;
      const r = looker.getBoundingClientRect();
      const x = ev.clientX - (r.left + r.width / 2), y = ev.clientY - (r.top + r.height / 2);
      const nx = Math.abs(x) > r.width * .6 ? Math.sign(x) : 0, ny = Math.abs(y) > r.height * .6 ? Math.sign(y) : 0;
      if (nx !== look[0] || ny !== look[1]) { look = [nx, ny]; paint(); }
    });
    lookEvt = e;
  }, { passive: true });

  // ---------- floating pixels, like the Lumi OS wallpaper. They only move while on screen.
  function pixels(canvas, count = 70) {
    const c = canvas.getContext("2d");
    const n = lowPower ? Math.round(count * 0.45) : count;
    const step = lowPower ? 2 : 1;            // frames per move: 30 fps on weak devices, at the same speed
    let w = 0, h = 0, dots = [], on = false, raf = 0, frame = 0;
    if (lowPower) canvas.style.imageRendering = "pixelated";
    const draw = () => {
      c.clearRect(0, 0, w, h);
      for (const d of dots) { c.fillStyle = `rgba(95,211,173,${d.a})`; c.fillRect(Math.round(d.x), Math.round(d.y), d.s, d.s); }
    };
    const size = () => {
      const cw = canvas.clientWidth, ch = canvas.clientHeight;
      if (cw === w && ch === h) return;      // a phone's address bar showing or hiding is not a new size
      const dpr = lowPower ? 1 : Math.min(2, devicePixelRatio || 1);
      w = cw; h = ch;
      canvas.width = w * dpr; canvas.height = h * dpr; c.setTransform(dpr, 0, 0, dpr, 0, 0);
      dots = Array.from({ length: n }, () => ({ x: Math.random() * w, y: Math.random() * h, s: 2 + Math.random() * 6,
        v: .08 + Math.random() * .3, a: .15 + Math.random() * .5 }));
      draw();
    };
    const tick = () => {
      raf = 0;
      if (!on) return;
      if (++frame % step === 0) {
        for (const d of dots) { d.y -= d.v * step; if (d.y < -10) { d.y = h + 10; d.x = Math.random() * w; } }
        draw();
      }
      raf = requestAnimationFrame(tick);
    };
    size();
    let rs = 0;
    addEventListener("resize", () => { if (!rs) rs = requestAnimationFrame(() => { rs = 0; size(); }); });
    new IntersectionObserver(([e]) => {
      on = e.isIntersecting;
      if (on && !reduced && !raf) raf = requestAnimationFrame(tick);
    }).observe(canvas);
  }
  document.querySelectorAll("canvas.pixels").forEach(c => pixels(c, +c.dataset.count || 70));

  // ---------- RU from the page itself, the rest from PAGE[lang] and UI[lang]
  const RU = {}, RU_ATTR = [];
  document.querySelectorAll("[data-i18n]").forEach(e => RU[e.dataset.i18n] = e.textContent);
  document.querySelectorAll("[data-i18n-html]").forEach(e => RU[e.dataset.i18nHtml] = e.innerHTML);
  // data-i18n-attr="alt:key; aria-label:key2" translates attributes
  document.querySelectorAll("[data-i18n-attr]").forEach(e => e.dataset.i18nAttr.split(";").forEach(pair => {
    const [attr, key] = pair.split(":").map(s => s.trim());
    if (!attr || !key) return;
    RU[key] = e.getAttribute(attr) || ""; RU_ATTR.push([e, attr, key]);
  }));
  const meta = document.querySelector('meta[name="description"]');
  RU.title = document.title; if (meta) RU.description = meta.content;

  const pick = l => l && LANGS[l.slice(0, 2).toLowerCase()] ? l.slice(0, 2).toLowerCase() : null;
  const urlLang = pick(new URLSearchParams(location.search).get("lang"));
  let lang = urlLang;
  if (!lang) try { lang = pick(localStorage.getItem("lang")); } catch (e) {}
  if (!lang) lang = (navigator.languages || [navigator.language || ""]).map(pick).find(Boolean) || "en";

  const t = key => {
    // PAGE.ru holds only what scripts write (the page's HTML already is Russian)
    if (lang === "ru") return (PAGE.ru || {})[key] ?? UI.ru[key] ?? RU[key];
    return (PAGE[lang] || {})[key] ?? UI[lang][key] ?? (PAGE.en || {})[key] ?? UI.en[key] ?? RU[key];
  };

  const ua = navigator.userAgent;
  const os = /Mac/.test(ua) && !/iPhone|iPad/.test(ua) ? "mac" : /Win/.test(ua) ? "win" : /Linux|X11/.test(ua) && !/Android/.test(ua) ? "linux" : null;

  // A ?lang= link (the hreflang alternates) is its own page for search engines, so its canonical says so.
  const canonical = document.querySelector('link[rel="canonical"]');
  const baseCanonical = canonical && canonical.href;

  function applyLang() {
    document.documentElement.lang = lang;
    document.querySelectorAll("[data-i18n]").forEach(e => { const v = t(e.dataset.i18n); if (v != null) e.textContent = v; });
    document.querySelectorAll("[data-i18n-html]").forEach(e => { const v = t(e.dataset.i18nHtml); if (v != null) e.innerHTML = v; });
    RU_ATTR.forEach(([e, attr, key]) => { const v = t(key); if (v != null) e.setAttribute(attr, v); });
    document.title = t("title");
    if (meta && t("description")) meta.content = t("description");
    if (canonical && urlLang) canonical.href = baseCanonical + "?lang=" + lang;
    syncMenu();
    renderStats();
    PAGE.onLang && PAGE.onLang(lang);
  }
  function setLang(l) {
    lang = l;
    try { localStorage.setItem("lang", l); } catch (e) {}
    if (urlLang) { const u = new URL(location.href); u.searchParams.set("lang", l); history.replaceState(null, "", u); }
    applyLang();
  }

  // ---------- the language menu: a button with the current code, a list of five. Arrows, Home/End, Esc work.
  const lb = document.getElementById("lang");
  let menu = null;
  if (lb) {
    lb.setAttribute("aria-haspopup", "true");
    lb.setAttribute("aria-expanded", "false");
    menu = document.createElement("ul");
    menu.className = "lang-menu"; menu.id = "lang-menu"; menu.setAttribute("role", "menu"); menu.hidden = true;
    lb.setAttribute("aria-controls", menu.id);
    menu.innerHTML = Object.entries(LANGS).map(([code, l]) =>
      `<li role="none"><button type="button" role="menuitemradio" data-lang="${code}" lang="${code}" tabindex="-1"><span class="mono">${code}</span>${l.name}</button></li>`).join("");
    const wrap = document.createElement("div");
    wrap.className = "langs";
    lb.replaceWith(wrap); wrap.append(lb, menu);
    const items = () => [...menu.querySelectorAll("button")];
    const open = (focusIdx) => {
      menu.hidden = false; lb.setAttribute("aria-expanded", "true");
      const list = items();
      (list[focusIdx ?? list.findIndex(b => b.dataset.lang === lang)] || list[0]).focus();
    };
    const close = (refocus) => { if (menu.hidden) return; menu.hidden = true; lb.setAttribute("aria-expanded", "false"); if (refocus) lb.focus(); };
    lb.addEventListener("click", () => menu.hidden ? open() : close(true));
    lb.addEventListener("keydown", e => {
      if (e.key === "ArrowDown") { e.preventDefault(); open(); }
      if (e.key === "ArrowUp") { e.preventDefault(); open(items().length - 1); }
    });
    menu.addEventListener("click", e => {
      const b = e.target.closest("button[data-lang]");
      if (!b) return;
      close(true);
      if (b.dataset.lang !== lang) setLang(b.dataset.lang);
    });
    menu.addEventListener("keydown", e => {
      const list = items(), i = list.indexOf(document.activeElement);
      const go = j => { e.preventDefault(); list[(j + list.length) % list.length].focus(); };
      if (e.key === "ArrowDown") go(i + 1);
      else if (e.key === "ArrowUp") go(i - 1);
      else if (e.key === "Home") go(0);
      else if (e.key === "End") go(list.length - 1);
      else if (e.key === "Escape") { e.preventDefault(); close(true); }
      else if (e.key === "Tab") close(false);
    });
    document.addEventListener("pointerdown", e => { if (!wrap.contains(e.target)) close(false); });
  }
  function syncMenu() {
    if (!lb) return;
    lb.textContent = lang.toUpperCase();
    lb.setAttribute("aria-label", t("ui_lang") + ": " + LANGS[lang].name);
    menu.querySelectorAll("button").forEach(b => b.setAttribute("aria-checked", String(b.dataset.lang === lang)));
  }

  // ---------- nav gets solid once the page scrolls
  const nav = document.querySelector(".nav");
  let solid = null;
  const onScroll = () => { const s = scrollY > 30; if (nav && s !== solid) { solid = s; nav.classList.toggle("solid", s); } };
  addEventListener("scroll", onScroll, { passive: true }); onScroll();

  // ---------- reveals
  const io = new IntersectionObserver(es => es.forEach(e => { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } }),
    { threshold: 0.15, rootMargin: "0px 0px -40px 0px" });
  document.querySelectorAll(".reveal").forEach(e => io.observe(e));
  // Tabbing onto something not yet revealed shows it at once.
  document.addEventListener("focusin", e => { const r = e.target.closest && e.target.closest(".reveal:not(.in)"); if (r) r.classList.add("in"); });

  // ---------- light under the pointer on cards (mouse and pen only; a finger dragging the page doesn't need it)
  let cardEvt = null;
  document.addEventListener("pointermove", e => {
    if (e.pointerType === "touch") return;
    if (!cardEvt) requestAnimationFrame(() => {
      const ev = cardEvt; cardEvt = null;
      const card = ev.target.closest && ev.target.closest(".card");
      if (!card) return;
      const r = card.getBoundingClientRect();
      card.style.setProperty("--mx", ev.clientX - r.left + "px"); card.style.setProperty("--my", ev.clientY - r.top + "px");
    });
    cardEvt = e;
  }, { passive: true });

  // ---------- scroll-driven scenes. Element positions are measured once and kept until the layout changes,
  // so a scroll frame only writes styles and never makes the browser lay the page out again.
  const geo = new Map();
  let dirty = true;
  function rect(el) {
    if (dirty) { geo.clear(); dirty = false; }
    let g = geo.get(el);
    if (!g) {
      let top = 0;
      for (let n = el; n; n = n.offsetParent) top += n.offsetTop;   // the layout box: transforms don't move it
      g = { top, height: el.offsetHeight };
      geo.set(el, g);
    }
    return { top: g.top - scrollY, height: g.height };
  }
  // scroll progress of a tall section with a sticky inside: 0 when it reaches the top, 1 when it leaves
  function progress(el) {
    const r = rect(el);
    return Math.min(1, Math.max(0, -r.top / Math.max(1, r.height - innerHeight)));
  }
  // and of any element passing through the viewport: 0 as it enters at the bottom, 1 when its centre is in the middle
  function entering(el) {
    const r = rect(el);
    return Math.min(1, Math.max(0, (innerHeight - r.top) / (innerHeight / 2 + r.height / 2)));
  }
  const scrollFns = [];
  let queued = false;
  const runScroll = () => { queued = false; scrollFns.forEach(f => f()); };
  const queue = () => { if (!queued) { queued = true; requestAnimationFrame(runScroll); } };
  addEventListener("scroll", queue, { passive: true });
  let lastW = innerWidth, lastH = innerHeight;
  addEventListener("resize", () => {
    // A phone's address bar changes the height by a few dozen pixels while scrolling: keep the measurements then.
    if (innerWidth !== lastW || Math.abs(innerHeight - lastH) > 120) dirty = true;
    lastW = innerWidth; lastH = innerHeight; queue();
  });
  if (window.ResizeObserver) new ResizeObserver(() => { dirty = true; queue(); }).observe(document.body);
  addEventListener("load", () => { dirty = true; queue(); });

  // ---------- live numbers from GitHub
  const counts = {};
  function renderStats() {
    document.querySelectorAll("[data-count]").forEach(el => {
      const n = counts[el.dataset.count];
      if (n == null) return;
      const box = el.closest("[data-stat]") || el;
      el.textContent = typeof n === "number" ? n.toLocaleString(LANGS[lang].locale) : n;
      const u = box.querySelector("[data-unit]");
      if (u && typeof n === "number") u.textContent = unit(el.dataset.count, n);
      box.hidden = false;
    });
    PAGE.onCounts && PAGE.onCounts(counts, lang);
  }
  // GitHub lets a visitor's address make 60 unsigned calls an hour, so answers are kept for an hour, and the last known
  // ones are shown when GitHub says no.
  const gh = "https://api.github.com/repos/AlbertS15/Lumi";
  const HOUR = 3600e3;
  function cached(key, url, pickFn) {
    let old = null;
    try { old = JSON.parse(localStorage.getItem("gh:" + key)); } catch (e) {}
    if (old && Date.now() - old.t < HOUR) return Promise.resolve(old.v);
    return fetch(url).then(r => r.ok ? r.json() : Promise.reject(r.status)).then(json => {
      const v = pickFn(json);
      try { localStorage.setItem("gh:" + key, JSON.stringify({ t: Date.now(), v })); } catch (e) {}
      return v;
    }).catch(() => old ? old.v : null);
  }
  cached("stars", gh, repo => repo.stargazers_count).then(n => { if (n != null) { counts.stars = n; renderStats(); } });
  if (document.querySelector('[data-count="downloads"], [data-count="releases"], [data-count="latest"]'))
    cached("releases", gh + "/releases?per_page=100", list => ({
      downloads: list.reduce((s, rel) => s + rel.assets.reduce((x, a) => x + a.download_count, 0), 0),
      releases: list.length,
      latest: (list.find(r => !r.prerelease) || list[0] || {}).tag_name || null,
    })).then(v => { if (v) { Object.assign(counts, v); if (counts.latest == null) delete counts.latest; renderStats(); } });

  // ---------- copy buttons
  document.querySelectorAll("[data-copy]").forEach(b => b.addEventListener("click", () => {
    const text = document.querySelector(b.dataset.copy).innerText.replace(/^\$ /gm, "").trim();
    navigator.clipboard && navigator.clipboard.writeText(text).then(() => {
      const old = b.textContent; b.textContent = t("copied");
      setTimeout(() => b.textContent = old, 1400);
    });
  }));

  // The page calls Site.applyLang() once its own script has set up what onLang needs.
  window.Site = { reduced, lowPower, os, LANGS, get lang() { return lang; }, get locale() { return LANGS[lang].locale; }, t, applyLang,
    rect, progress, entering, onScroll: f => { scrollFns.push(f); f(); }, drawGhost, cached };
})();
