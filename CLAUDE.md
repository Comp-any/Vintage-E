# Notes for Claude

More about this repository is in the "vintage story mods" project docs (claude/vintage-e-top50-test.md and claude/projecte-vs-vintage-e.md).

## Waiting for CI without using up GitHub's limit

Wait for a run with `gh run watch <run id> --interval 60 --exit-status`, never with the default. The default refreshes
every 3 seconds, and while a run is in progress each refresh reads the run, its jobs, the workflow and annotations:
14 API requests in 10 seconds when measured on 2026-11-03, about 5,000 an hour. A session's GitHub requests go
through the Claude GitHub App as demonic69, and GitHub counts them against the account's personal 5,000 an hour, which
the user's own tokens share (the Mod DB Uploader app, Vintage Auto Update in the game). On 2026-11-03 sessions waiting
on builds used it up twice, and the app stopped working until the reset. `--interval 60` is about 300 an hour and
needs no `sleep`.

- Take the run id from the URL `gh workflow run` prints; don't poll `actions/runs` for it.
- Never spin an `until` loop on `gh api` (or `git ls-remote`): it asks as fast as it can. Wait on the run instead.

## The doctor and Vintage Doctor (1.26.0)

- With the mod Vintage Doctor (github.com/Comp-any/Vintage-Doctor, 1.2.0+) on the server and on a player's game, the
  EMC doctor is shown in its window: `src/Diagnostics/DoctorBridge.cs` answers its event-bus questions with the
  reports `EmcDiagnose.Run(..., handover: true)` builds. Vintage-E never references that mod's DLL. Without it (or
  with it on one side only) Vintage-E's own window is used, so both ways have to keep working.
- A new page or fix of the doctor needs nothing extra: the bridge turns any report's links into Vintage Doctor's.
  A new kind of link (anything but `{{action|arg|label}}`, `{{fix-what|...}}`, `{{setvalue|...}}`,
  `{{copy|text|label}}`, `[[i:code]]`, `[[b:code]]`) needs a case in `DoctorBridge.Convert`; the self-test
  (`SelfTestHandover.cs`) fails on a link Vintage Doctor wouldn't know.
- This repository is public and Vintage Doctor's is private: don't put its code on a branch here (a results
  branch included). How it reads what a mod hands it is in its docs/api.md and the VintageDoctor.xml in its zip.
- Copy buttons (1.26.7): a new finding needs nothing either. `EmcDiagnose.Run` ends with `AddCopyButtons`
  (`EmcDiagnoseCopy.cs`) whenever a player asked (a window will show the page): a `{{copy|text|label}}` line after
  each warning and problem, and each note with text behind it, holding that finding in plain words with the
  versions. The console and the saved files get the page without them. Vintage Doctor takes it as
  `{{copy|text||label}}` (the text is the target) and cuts any handed-over line at 4,000 characters, hence
  `CopyTextMax`.
- Vintage Doctor 1.7.0 and newer puts its own *Copy this for a report* on every card that isn't a pass, so a page
  for its window is built without Vintage-E's (1.26.9, `DoctorBridge.CopyButtonsFor`). The window is the player's:
  their game sends its Vintage Doctor version along (`EmcDiagnoseRequestPacket.ClientDoctorVersion`), and the
  server's own decides when theirs didn't say. Vintage-E's own window and a Vintage Doctor before 1.7.0 still get
  them. `DoctorBridge.TestCopyButtons` forces them on for the test's last console step, whatever the version.
- Test both ways: `gh workflow run analyze.yml ... -f doctor=true` installs the newest Vintage Doctor release
  (`.github/scripts/get-doctor.sh`: its repository is public since 2026-11-25, so no token is needed; the copy in
  `tools/vendor/vintagedoctor/` is only the fallback when the download fails, and `doctor-version.txt` in the results
  says which ran) and the self-test then uses its `/doctor` command from the console; without the input the same
  checks run against the bridge directly. When Vintage Doctor's event-bus protocol changes, still put its new zip there.
- The copy there is Vintage Doctor 1.3.0 (2026-11-22). Its check-up is laid out by mod, so Vintage-E's card and
  the button for the EMC pages are under a "Vintage-E <version>" heading, and it runs every check each time (no
  "deeper" round). It writes out symbols the game's font may lack in whatever a mod hands it, so a report's marks
  and arrows can stay as they are. Nothing in the protocol changed: 1.2.0 still works with the bridge.
