# Ask my files (experimental)

Type a question in the bar, pick **Ask my files** (or press Enter on that row) and Lumi answers from your own documents, naming the files it used; click a file to open it.

- **Off by default.** It turns on when you add a folder in the welcome window. Remove all folders to turn it off.
- **Fully offline.** Only the Ollama on this computer (`127.0.0.1:11434`) is used, even when a cloud model is chosen for chat. A cloud/remote Ollama model is never used for this feature.
- **Files:** `.txt`, `.md`, `.pdf` (text layer; scans are skipped), `.docx`. Files over 20 MB are skipped.
- **Models:** `embeddinggemma` (multilingual, ~620 MB, offered as a download) turns text into vectors. The answer comes from your chosen model if it is local, otherwise from the best local Lumi model that fits.
- **Answers only from the excerpts:** the top 5 pieces go to the model with an instruction to say it has no information when they don't contain the answer.

## How it works

| Part | Where |
|---|---|
| Chunking (~450 characters, 80 overlap, cut at paragraph/sentence/word) | `Chunker` in `windows/Lumi/Library.cs` |
| Text from files | `DocText` (PdfPig for PDF, `System.IO.Compression` + XML for docx) |
| Embeddings | `LocalOllama.EmbedAsync` → `POST /api/embed` |
| Index | `LibraryIndex`, one binary file in the app data folder (user-only permissions on Linux); only new or changed files (size or modified time) are re-read; re-checked every 20 minutes and on every folder change |
| Ranking | `Ranking.Top`, cosine similarity |
| Answering | `FilesRunner`, `LibraryPrompt` in `windows/Lumi/FilesRunner.cs` / `Library.cs` |
| UI | `LauncherWindow` (row, sources, note), `WelcomeWindow` (folders card) on Windows; the same in `linux/Lumi` |

`windows/Lumi/Library.cs` and `FilesRunner.cs` have no UI code and are shared with the Linux app by `Compile Include`. Tests: `dotnet test windows/Lumi.Tests` (also run by CI).

## Porting to macOS (not done yet)

The Swift app has the strings (`Sources/Strings.swift`, generated from `strings/strings.json`) but no feature code. It needs:

1. **Settings:** `libraryFolders: [String]` and `libraryEmbedModel` (default `embeddinggemma`) in the settings store; a folder card in the welcome window using `NSOpenPanel` (`canChooseDirectories`, multiple selection). App Sandbox is off, so no security-scoped bookmarks are needed (check this if the app is ever sandboxed).
2. **Text extraction:** `.txt`/`.md` via `Data` with UTF-8, then UTF-16 (BOM), then Windows-1251 fallback; `.pdf` via PDFKit (`PDFDocument.string`); `.docx` by unzipping `word/document.xml` (e.g. `/usr/bin/unzip -p`, or `Process`) and reading `w:t` runs with `XMLParser` (paragraph end → blank line, `w:tab` → space, `w:br` → newline; do not resolve external entities).
3. **Chunker:** port `Chunker.Split` exactly (450/80, break priority paragraph → sentence/line → space) and port the test cases from `windows/Lumi.Tests/ChunkerTests.cs` into an XCTest or a check in `build.sh`.
4. **Ollama:** `GET /api/tags` (skip models with `remote_host`/`remote_model` or `cloud` in the name, and non-`completion` capabilities), `POST /api/embed` with `truncate: true` and the per-model prefixes in `LocalOllama.Prefix`, `POST /v1/chat/completions` streaming for the answer with `reasoning_effort: "none"`. Use a fixed `127.0.0.1:11434` URL.
5. **Index:** same binary layout (`LUMI-LIBRARY-1`) or any format; keep it in `~/Library/Application Support/Lumi` with mode 0600. Brute-force cosine over all chunks is fast enough up to tens of thousands of chunks (use Accelerate `vDSP_dotpr` if needed).
6. **UI:** an "Ask my files" row in the bar when folders are set (below "Ask in ChatGPT"), sources as clickable links opening with `NSWorkspace.shared.open`, a "🔒 answered on this computer by <model>" note, follow-ups staying in files mode.
