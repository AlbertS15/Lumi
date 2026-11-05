"""Builds site/changelog.html from release-notes/*.html (the Telegram-style posts), newest first.
Run: python3 scripts/build_changelog.py
Dates come from the git tag of each version. The notes stay as written (Russian); the page chrome is translated in the browser.
"""
import html, pathlib, re, subprocess

root = pathlib.Path(__file__).resolve().parent.parent
BASE = "https://alberts15.github.io/Lumi/"


def version_key(p):
    return tuple(int(x) for x in re.findall(r"\d+", p.stem))


def tag_date(tag):
    try:
        return subprocess.check_output(["git", "log", "-1", "--format=%cs", tag], cwd=root, text=True, stderr=subprocess.DEVNULL).strip()
    except Exception:
        return ""


def render(text):
    """Telegram HTML (<b> <i> <code>, '• ' bullets, blank lines) -> paragraphs and lists. Everything else is escaped."""
    title = ""
    blocks = [b.strip("\n") for b in re.split(r"\n\s*\n", text.strip())]
    out, items = [], []

    def inline(s):
        s = html.escape(s, quote=False)
        return re.sub(r"&lt;(/?)(b|i|code)&gt;", r"<\1\2>", s)

    def flush():
        nonlocal items
        if items:
            out.append("<ul>" + "".join(f"<li>{i}</li>" for i in items) + "</ul>")
            items = []

    first = True
    for block in blocks:
        for line in block.split("\n"):
            line = line.strip()
            if not line:
                continue
            if first:
                first = False
                m = re.match(r"^<b>(.*?)</b>(.*)$", line)
                title, rest = (m.group(1), m.group(2).strip()) if m else (re.sub("<[^>]+>", "", line), "")
                title = html.escape(re.sub("<[^>]+>", "", title)).strip()
                if rest:
                    out.append(f"<p>{inline(rest)}</p>")
            elif line.startswith("• "):
                items.append(inline(line[2:]))
            else:
                flush()
                if re.fullmatch(r"<b>.*</b>", line):
                    out.append(f"<h3>{inline(line[3:-4])}</h3>")
                else:
                    out.append(f"<p>{inline(line)}</p>")
        flush()
    return title, "\n".join(out)


entries = []
for p in sorted((root / "release-notes").glob("v*.html"), key=version_key, reverse=True):
    title, body = render(p.read_text(encoding="utf-8"))
    ver = p.stem
    entries.append(f"""    <article class="card rel reveal" id="{ver}" lang="ru">
      <header><span class="mono ver">{ver}</span><time class="mono" datetime="{tag_date(ver)}">{tag_date(ver)}</time></header>
      <h2>{title}</h2>
      {body}
      <a class="more" href="https://github.com/AlbertS15/Lumi/releases/tag/{ver}" lang="en">GitHub release →</a>
    </article>""")

latest = version_key(sorted((root / "release-notes").glob("v*.html"), key=version_key)[-1])
page = (root / "scripts" / "changelog.template.html").read_text(encoding="utf-8")
page = page.replace("<!--ENTRIES-->", "\n".join(entries)).replace("@@COUNT@@", str(len(entries)))
(root / "site" / "changelog.html").write_text(page, encoding="utf-8")
print("changelog.html:", len(entries), "releases")
