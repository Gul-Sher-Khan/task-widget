# How Wispr Flow delivers text into the Capture box

Research for issue #5 (child of map #1). Researched 2026-10-06 against the Wispr Flow Help Center at `docs.wisprflow.ai`. Wispr publishes no public changelog page (`wisprflow.ai/changelog` returns 404), so the help center is the only first-party source. Help-center articles are revised often; the "last updated" dates below are as fetched.

Labels used below:

- **[Doc]**: stated in an official Wispr Flow help article.
- **[Inference]**: my reading of documented behaviour. Not stated by Wispr.
- **[Forum]**: found only in a forum or third-party site.

## Short answer

1. On Windows, Flow inserts text **by pasting through the clipboard** into whatever field has keyboard focus. It does not type characters one by one. [Doc]
2. Any standard text field that accepts a normal **Ctrl+V** paste should work. That covers a native Win32/WinUI edit box and a `<textarea>` / `<input>` inside WebView2. Wispr names no supported or unsupported UI frameworks. [Inference from Doc]
3. Flow **can press Enter after a dictation**. Two first-party options exist, both off by default: the experimental **"press enter" voice command**, and a **Press Enter** shortcut you can bind to a key or mouse button. [Doc]
4. There is **no public API** for dictation or text insertion. Flow's only documented integration point is a read-only remote **MCP server** for Notetaker meeting data, which has nothing to do with dictation. [Doc]
5. Effect on the Capture box: the app gets **no "dictation finished" signal**. It only sees one or more paste events landing in its text field. A Capture should be committed by **Enter** (pressed by the user, said as "press enter", or sent by a bound Press Enter button). Do not try to guess completion from typing pauses.

## 1. How Flow inserts text on Windows

**It pastes through the clipboard.** "Flow pastes text using the clipboard" on Mac and Windows ([What to expect from Flow](https://docs.wisprflow.ai/articles/4048537120-what-to-expect-from-flow-accuracy-and-known-limitations)). "Desktop Flow temporarily uses the clipboard" ([Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation)). [Doc]

**It restores the clipboard afterwards.** After a successful paste, Flow puts the previous clipboard contents back "about half a second" later. For remote-desktop viewers (RDP, Citrix, VNC and similar) it waits about five seconds. On Windows the restore covers text, HTML and common images, but not file drops, PDF, RTF, wave audio or CSV. Restored items keep their "sensitive content" marking, so they stay out of Win+V clipboard history. If insertion fails, the transcript can be left on the clipboard ([Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation); [non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts)). [Doc]

**It pastes formatted text when the target accepts it.** Desktop snippets keep bold, italics, lists, links and line breaks: "Compatible apps receive formatted text; others receive plain text" ([Snippets troubleshooting](https://docs.wisprflow.ai/articles/4816874402-troubleshooting-guide-for-snippets-pasting-only-part-of-the-text-or-code)). History copies "rich text and plain-text markdown". [Doc] So the clipboard can carry HTML alongside plain text. [Inference]

**It sends a paste keystroke (most likely a synthetic Ctrl+V).** Wispr never says which key it sends. These documented facts point to a synthetic Ctrl+V sent with `SendInput`:

- "Windows pasting is layout-independent." [Doc]
- "Held modifier keys can interfere with Windows pasting." [Doc]
- Some terminals and IDEs need "Ctrl+Shift+V or right-click paste instead of Ctrl+V", and that is given as a reason pasting fails there. [Doc]
- Pasting into an elevated app fails with "Update admin settings to paste!" / "Run Flow as Administrator to insert text everywhere" ([non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts)). [Doc] This matches Windows UIPI, which blocks synthetic input from a lower-integrity process into a higher-integrity window. [Inference]

**It reads the field to confirm the paste.** Flow checks afterwards whether the text arrived. "In a small set of apps whose text fields never report their contents back to Flow, Flow assumes insertion succeeded and does not copy to the clipboard." It also detects "pasting of old clipboard contents" ([non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts)). Context Awareness reads "textbox contents (before, selected, and after the cursor)", and on Windows this "requires none" of the accessibility permissions that Mac needs ([Context Awareness](https://docs.wisprflow.ai/articles/4678293671-feature-context-awareness)). [Doc] On Windows this reading is almost certainly done through **UI Automation**, but Wispr does not name the API. [Inference]

**Long text can arrive in several pastes.** "long or multi-line text can arrive in pieces, and a failure can leave only part inserted" ([Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation)). Long recordings are processed "in several parts" ([What to expect](https://docs.wisprflow.ai/articles/4048537120-what-to-expect-from-flow-accuracy-and-known-limitations)). [Doc]

**Text appears all at once, not word by word.** "Desktop dictation inserts the completed text after you stop, rather than displaying a live word-by-word transcript as you speak" ([What is Wispr Flow?](https://docs.wisprflow.ai/articles/2772472373-what-is-flow)). [Doc]

**Flow inserts only into the focused field.** "Flow only inserts into a focused field." Users are told to keep "the intended field focused until processing and insertion finish" ([non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts); [Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation)). [Doc]

**Flow adjusts the text to fit what is already in the field.** Context-aware formatting matches surrounding casing, spacing and punctuation. Continuing mid-sentence lowercases the first letter and adds a leading space. This runs on-device even when Context Awareness is off ([Context Awareness](https://docs.wisprflow.ai/articles/4678293671-feature-context-awareness)). [Doc] Inserted text therefore depends on what the Capture box already contains.

## 2. Native windows and WebView2

Wispr does not mention WebView2, WinUI, WPF or Win32 anywhere. What follows is inferred from the mechanism above.

- **Native edit control** (Win32 `EDIT`/`RichEdit`, WinUI `TextBox`, WPF `TextBox`): handles Ctrl+V and exposes its value through UI Automation. Should work. [Inference]
- **WebView2 `<textarea>` / `<input>` / `contenteditable`:** WebView2 is Chromium, and Flow supports Chromium browsers and Electron apps such as Slack, Cursor and VS Code ([Context Awareness](https://docs.wisprflow.ai/articles/4678293671-feature-context-awareness); [IDEs](https://docs.wisprflow.ai/articles/6434410694-use-flow-with-cursor-vs-code-and-other-ides)). Chromium exposes form fields to UI Automation, so confirmation should also work. Should work. [Inference]
- **Custom-drawn text input** with no real edit control (for example a canvas-rendered field): may accept Ctrl+V but report no text to UI Automation. That is the "small set of apps" case, where Flow cannot confirm insertion. Avoid. [Inference]

Things that will break insertion in our own window:

- **Running Task Widget elevated.** Flow then needs "Run as administrator" to paste into it. Ship and run the app at normal integrity. [Doc + Inference]
- **The Capture box not holding foreground focus** while Flow pastes. A window opened by a global hotkey must actually become the foreground window and focus its text field. [Inference]
- **Closing the Capture box when it loses focus.** Flow's own Flow Bar overlay could take focus during dictation, though Wispr does not document whether it does. Test this before relying on close-on-blur. [Inference]
- **Swallowing Ctrl+V** or handling paste in a non-standard way, for example rejecting HTML clipboard formats. [Inference]

**Hotkey clash:** Flow's Windows defaults are Push-to-talk **Ctrl+Win**, Hands-free **Ctrl+Win+Space**, Command Mode **Ctrl+Win+Alt**, Paste last transcript **Shift+Alt+Z**, Copy last transcript **Shift+Alt+X**, Cancel **Escape** ([Shortcuts](https://docs.wisprflow.ai/articles/2612050838-supported-unsupported-keyboard-hotkey-shortcuts)). [Doc] The Capture box hotkey must avoid all of these. Escape is also Flow's cancel key during dictation, which matters if Escape closes the Capture box. [Inference]

## 3. Pressing Enter after dictation

Both options below are first-party and documented.

**"Press enter" voice command.** On Mac and Windows it is "off by default and lives in Flow Hub → Settings → Experimental". It is visible only with an active personal or enterprise subscription. Once enabled, "end dictation with 'press enter' to remove the phrase and press Enter after a successful paste". Said mid-sentence, or in Command Mode, the phrase is kept as text. The first time Flow detects it, a one-time prompt appears (Great / Disable) instead of pressing Enter ([Use Flow hands-free](https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free)). [Doc]

**Press Enter shortcut.** In Settings → Shortcuts there is a **Press Enter** action. It "starts unbound and sends Enter on press, not release". A mouse binding passes extra modifiers through, so Shift + mouse button gives Shift+Enter. Reset to default clears it ([Shortcuts](https://docs.wisprflow.ai/articles/2612050838-supported-unsupported-keyboard-hotkey-shortcuts); [Command Mode](https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode)). [Doc]

Notes:

- Free users cannot use the voice command, because the Experimental setting needs a subscription. [Doc] The shortcut row has no documented plan restriction, but I could not confirm one way or the other.
- Flow presses Enter *after a successful paste*, so the Capture box receives paste(s) followed by Enter in that order. [Doc] The exact delay between the two is not documented.
- An earlier community post on the Mac Power Users forum ([talk.macpowerusers.com](https://talk.macpowerusers.com/t/wisprflow-tip-press-enter-to-trigger-return/45142)) describes the same voice command. [Forum] The official article above replaces it as the source.

## 4. API and integration points

- **No public dictation or insertion API** appears anywhere in the help center (full index: [llms.txt](https://docs.wisprflow.ai/llms.txt)). No webhook, SDK, local IPC or "dictation finished" event is documented. [Doc, by absence]
- The **only** integration point is a **remote MCP server** at `https://api.wisprflow.ai/connect/mcp`. It is read-only and covers Notetaker meetings, notes, Scratchpad and calendar. It explicitly cannot read dictation history ([Connect an MCP client](https://docs.wisprflow.ai/articles/9551372685-connect-an-mcp-client-to-wispr-flow-remote-mcp-server)). [Doc] It is no use for the Capture box.
- Third-party aggregator sites (e.g. smallest.ai, apis.apievangelist.com) claim Wispr offers a "developer-friendly API" with webhooks, or list scraped OpenAPI specs for `api.wisprflow.ai`. None of this is backed by Wispr documentation, and it looks like Flow's private backend. [Forum/third-party, unverified] Do not build on it.

## 5. Known compatibility issues (Windows)

All from the official help center. [Doc]

| Issue | Source |
|---|---|
| Elevated target app → "Update admin settings to paste!"; fix is running Flow as admin | [non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts) |
| Apps that read the clipboard after more than ~0.5 s may paste the *old* clipboard contents | [What to expect](https://docs.wisprflow.ai/articles/4048537120-what-to-expect-from-flow-accuracy-and-known-limitations) |
| Terminals/IDEs that need Ctrl+Shift+V do not receive the paste | [Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation) |
| Held modifier keys interfere with pasting | [non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts) |
| Long or multi-line text arrives in pieces; can be partly inserted on failure | [Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation) |
| Fields that do not report their contents: Flow assumes success and gives no clipboard fallback | [non-QWERTY layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts) |
| Windows on ARM unsupported (x64 only); Windows setup asks only for Microphone access | [Supported devices](https://docs.wisprflow.ai/articles/1036674442-supported-devices-and-system-requirements) |
| Keyboard utilities can intercept Flow's shortcuts | [Shortcuts](https://docs.wisprflow.ai/articles/2612050838-supported-unsupported-keyboard-hotkey-shortcuts) |
| Recovery: Paste last transcript (Shift+Alt+Z) re-pastes the last dictation | [Fix text not pasting](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation) |

## 6. What this means for detecting a finished Capture

What the Capture box can observe: it is opened, the user dictates, then one or more paste events (plain text and/or HTML) land in its focused field. If the user has turned it on, an Enter key press follows. Nothing tells the app that Flow has finished. Recommendations (all [Inference]):

1. **Enter commits the Capture. Shift+Enter inserts a newline.** This fits all three ways Enter can arrive: the user presses Enter, says "press enter", or uses a bound Press Enter button. Users who want hands-free submit can turn on Flow's feature. We do not need to build anything for it.
2. **Do not commit on the first paste or after a quiet period.** Long dictations arrive as several pastes, and a user may dictate twice into one Capture. A timeout would cut Captures short. At most, offer an optional, generous auto-commit later, and only once the field has been idle for a while.
3. **Handle paste through the normal path and turn rich text into plain text.** In WebView2, use a `<textarea>` (plain text by nature), or handle the `paste` / `beforeinput` (`insertFromPaste`) event on a `contenteditable`. In native code, a plain-text edit control does this for you. Never read the clipboard directly to fetch the dictation, because Flow restores the old contents about 0.5 s later.
4. **Use a real, UI Automation-visible text control.** Flow can then confirm insertion and fall back to the clipboard when it fails. That rules out canvas-drawn custom inputs.
5. **Keep focus stable from when the box opens until Enter.** Make the Capture box foreground on hotkey with the caret in the field. Do not close it on blur until we have tested what the Flow Bar does. Do not run the app elevated.
6. **Treat Escape with care.** Flow uses Escape to cancel an in-progress dictation. If Escape also closes the Capture box, one key press could cancel both. Consider making Escape close the box only when the field is empty, or asking for a second Escape.
7. **Keep the Capture hotkey clear of Flow's defaults** (Ctrl+Win, Ctrl+Win+Space, Ctrl+Win+Alt, Shift+Alt+Z/X).
8. **Optional extra:** the text can show up as one big paste, so a brief "received" animation on paste helps the user see it arrived.

## Open questions to test in a prototype

- Does showing the Flow Bar or its notifications take foreground focus from our window on Windows? (Decides whether close-on-blur is safe.)
- How long after the last paste does "press enter" send Enter? Could a fast Enter handler fire before a late paste chunk arrives?
- Does Flow's insertion confirmation work against our chosen control (WinUI `TextBox` vs WebView2 `<textarea>`)? Test with a deliberately failed paste and check that the clipboard fallback notice appears.
- Is the Press Enter shortcut row available on the free plan?

## Sources

All first-party unless marked.

- [Fix text not pasting after dictation](https://docs.wisprflow.ai/articles/7971211038-fix-text-not-pasting-after-dictation)
- [Flow fails to detect text fields or inserts incorrectly on non-QWERTY keyboard layouts](https://docs.wisprflow.ai/articles/1621472516-flow-fails-to-detect-text-fields-or-inserts-incorrectly-on-non-qwerty-keyboard-layouts)
- [What to expect from Flow: accuracy and known limitations](https://docs.wisprflow.ai/articles/4048537120-what-to-expect-from-flow-accuracy-and-known-limitations)
- [Use Flow hands-free](https://docs.wisprflow.ai/articles/6391241694-use-flow-hands-free) (includes "Press Enter without touching the keyboard")
- [Customize keyboard and mouse shortcuts](https://docs.wisprflow.ai/articles/2612050838-supported-unsupported-keyboard-hotkey-shortcuts)
- [How to use Command Mode](https://docs.wisprflow.ai/articles/4816967992-how-to-use-command-mode)
- [Context Awareness](https://docs.wisprflow.ai/articles/4678293671-feature-context-awareness)
- [Use Flow with Cursor, VS Code, and other IDEs](https://docs.wisprflow.ai/articles/6434410694-use-flow-with-cursor-vs-code-and-other-ides)
- [Troubleshooting Guide for Snippets Pasting Only Part of the Text or Code](https://docs.wisprflow.ai/articles/4816874402-troubleshooting-guide-for-snippets-pasting-only-part-of-the-text-or-code)
- [What is Wispr Flow?](https://docs.wisprflow.ai/articles/2772472373-what-is-flow)
- [Supported devices and system requirements](https://docs.wisprflow.ai/articles/1036674442-supported-devices-and-system-requirements)
- [Connect Claude, ChatGPT or another MCP client to Wispr Flow](https://docs.wisprflow.ai/articles/9551372685-connect-an-mcp-client-to-wispr-flow-remote-mcp-server)
- [Help Center index (llms.txt)](https://docs.wisprflow.ai/llms.txt)
- [Forum] [WisprFlow tip: "press enter" to trigger return, Mac Power Users](https://talk.macpowerusers.com/t/wisprflow-tip-press-enter-to-trigger-return/45142)
