# SOP Recorder for Windows 11

Updated in place from the first window-only prototype. Double-click **SopRecorder.exe** or **Launch.cmd**. See [START-HERE.txt](START-HERE.txt) for Mom's workflow.

## Recording

**Record Screen** is the primary action. It starts without a task name, overview, or window picker. The default is the primary display or the last selected display if still valid. **Choose target...** lists displays and optional individual application windows. Display mode captures the entire selected display across app switches. On start the recorder minimizes; return from the taskbar to Pause, Resume, or Stop. An initial screenshot anchors the recording. If Windows denies input-hook installation, the app stays visible and explicitly labels the session **MANUAL ONLY**; **Capture now** remains available.

Automatic triggers while recording:

- One screenshot per mouse click (left, right, middle, extra buttons); mouse-up follows a press in scope. Drags beyond eight pixels and wheel scrolling are ignored. A double click generates two click events.
- One screenshot at the beginning of a typing burst. The next burst begins after the configured idle period since the last non-repeated typing key event; default **2 seconds**. Letters, digits, ordinary punctuation, space, editing keys and IME process keys qualify. Keyboard shortcuts such as Ctrl+V are ignored without inspecting the clipboard.
- One screenshot for each discrete Tab or Shift+Tab press, including during an existing burst. These do not read or update the typing-burst timer.

Click and focus-change captures use a configurable **250 ms** delay by default; typing bursts request capture immediately. Repeated held keys, modifier-only input, and the recorder's own windows are excluded. Low-level hooks run on a separate message thread. They classify input into generic triggers and return without capturing or writing files. Only a transient held-key set suppresses auto-repeat. It is cleared when the hook thread ends. There is no text conversion, key-code serialization, clipboard access, or raw input log. Screenshots can of course contain visible text from the apps being recorded.

Pause and Stop uninstall input hooks, reset burst state on the next Resume, revoke file tracking, and cancel delayed/queued captures. In-flight results are discarded and their PNGs removed if the recording was paused/stopped before acceptance. Capture work is serialized to bound memory; frame results are still sorted by actual acquisition timestamp and sequence, not completion time. Capture or monitor errors pause recording. A disconnected target never silently switches to a different display while recording.

Keyboard display routing prefers the app's system caret position. When unavailable, it uses the foreground window's center. That fallback can be imprecise for a window spanning multiple monitors. Individual-window mode ignores other windows and separate pop-ups; display mode is the recommended workflow across apps.

## File-open evidence

The supported verified workflow in this version is **Task details → Open text file with verified evidence...**, while recording a display:

1. The user explicitly chooses a local `.txt` file in the viewer's Open dialog.
2. A separate local SOP text-viewer process opens that file, reads its contents (up to 1 MB), and obtains its canonical location with `GetFinalPathNameByHandle` from the **same handle** supplying the displayed content.
3. After displaying the document, it returns a one-use local metadata handoff containing only the verified path, acquisition time, process identity and viewer handle. The recorder validates the request nonce and viewer identity, and requires the viewer to be on the selected display.
4. A timestamped **file-only evidence step** is added. Automatic screenshot triggers may separately capture the visible viewer. The opened file is never bundled into the evidence export.

No filesystem watcher, recent-file list, title-to-path inference, preview detection or background read is treated as proof of an open. Other app workflows, including ordinary Explorer, Notepad, Office and browser file opens, are **not automatically verified in this version**. They never block screenshots or export. Enter their locations manually in the step's review field. A changed or shortened verified path is marked **manually entered or edited**. Clearing the field omits the path from Markdown; removing the step excludes all its metadata. Paths visible inside screenshots need those screenshots removed too; there is no pixel redaction tool.

Pause/Stop cancels outstanding handoffs. A viewer already opened remains usable, but a late file load cannot add evidence. Viewers do not monitor subsequent opens or create background-open evidence.

## Review, timestamps and timings

Stop, preview/enlarge each image, reorder, annotate, remove unwanted steps, and optionally improve the task name and overview. **Undo** restores the most recent removal. Any evidence edit clears review confirmation. Check the review box before exporting. **Record more** can extend a stopped session; its elapsed total runs from the first Start to the final Stop and includes the intervening review gap as inactive time.

`Stopwatch` provides monotonic durations, independent of wall-clock changes. Wall timestamps are taken immediately after pixel acquisition, before PNG encoding. Optional window mode obtains that timestamp inside its capture worker, rather than after the parent receives the saved file.

Each exported image has a footer **below** the original captured pixels, with local date, time to the second, UTC offset, and active time since Start. The same timestamp appears beside its Markdown reference. Timing summaries include Start-to-Stop elapsed time, active time excluding pauses, inactive time, active time before the first and after the last retained image, each consecutive active interval, and the longest interval between images. Calculations use original capture order even when export steps are reordered. Deleted screenshots and their timestamps are omitted; resulting gaps are labeled **time between retained captures**. Gaps can include idle time or unrecorded activity and are not work-effort estimates. Before-first + consecutive retained intervals + after-last equals total active time, subject to millisecond rounding. File-only steps do not participate in image interval calculations.

The ZIP contains only `procedure.md` and sequential `images/001.png`, etc. Markdown includes optional overview, notes, reviewed paths with verification status, matching relative links, timestamps and timing summaries. It asks the receiving AI agent to draft an SOP from evidence and flag missing decisions and exceptions. There is no account, backend, network upload, built-in AI, audio or transcription.

## Launch, build and tests

Requirements: Windows 11 x64 with its built-in .NET Framework 4.x and an unlocked interactive desktop. No .NET SDK or dependency restore is needed. This locally built executable is unsigned; it does not request administrator access or alter Windows security policy.

```powershell
# From this project folder:
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test.ps1
```

The execution-policy option applies only to that PowerShell process. The source is in `src/`. Test runs write `tests/test-results.txt`, synthetic sample evidence ZIPs, and UI/footer previews. See [VALIDATION.md](VALIDATION.md) for actual results and unverified Windows interactions. The test runner has a two-minute timeout and only launches test-owned viewer/dummy application processes.

## Storage and platform limits

Working PNGs and one-use file metadata live under `%TEMP%\SopRecorder\<session-id>`. Notes and task details stay in memory until export. Successful metadata handoffs are immediately deleted; cancellation revokes their permits and deletes pending handoffs. Normal close/new task cleans the temporary session directory. A crash can leave temporary evidence there; no saved drafts or crash recovery are provided. Preferences under `%LOCALAPPDATA%\SopRecorder\capture-settings.txt` contain only display selection and delay/idle settings. Export ZIPs stay where saved; network destinations are refused. Separate cloud-sync software may still sync a chosen local folder.

Native screen capture uses the selected display's visible pixels; occluded app content is not reconstructed. Optional window capture uses `PrintWindow` with a six-second worker timeout. Protected content, elevated apps, secure/UAC desktops, locked desktops, remote-session changes and some GPU-rendered windows can produce missing/blank images or fail. Visual review remains necessary. The hook callbacks are fast but Windows may suppress them under policy, privilege boundaries, desktop changes or heavy load. There is no permanent background service. High input rates can queue captures and shift their acquisition later than the configured delay; timestamps always identify actual acquisition. Multi-monitor/mixed-DPI behavior and physical cross-app input still need a normal interactive Windows acceptance run in the current delivery environment.

API references: [Microsoft low-level keyboard hooks](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc), [PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow), [GetFinalPathNameByHandle](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew).

## License and terms

First-party source and build/test scripts use the MIT license in LICENSE. Names, branding, artwork, legal documents and third-party material are excluded from that code grant. The embedded agreement is also included as terms.md. Acceptance applies to the official build and does not limit MIT or separate license rights. Assent is stored per Windows user under %LOCALAPPDATA%\ScreenQuill\terms-acceptance.txt, independently of app updates and recordings. Use the Terms button to review it again. Windows/.NET Framework is installed separately; see THIRD-PARTY-NOTICES.md.
