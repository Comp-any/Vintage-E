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
  A new kind of link (anything but `{{action|arg|label}}`, `{{fix-what|...}}`, `{{setvalue|...}}`, `[[i:code]]`,
  `[[b:code]]`) needs a case in `DoctorBridge.Convert`; the self-test (`SelfTestHandover.cs`) fails on a link
  Vintage Doctor wouldn't know.
- Test both ways: `gh workflow run analyze.yml ... -f doctor=true` installs the copy of Vintage Doctor's zip in
  `tools/vendor/vintagedoctor/` (its repository is private, so the test can't download it) and the self-test then
  uses its `/doctor` command from the console; without the input the same checks run against the bridge directly.
  With the organisation secret `DOCTOR_TOKEN` (read-only on the private Comp-any/Vintage-Doctor) the test downloads
  the newest release instead (`.github/scripts/get-doctor.sh`; `doctor-version.txt` in the results says which ran),
  so the copy is only the fallback. When Vintage Doctor's event-bus protocol changes, still put its new zip there.
- The copy there is Vintage Doctor 1.3.0 (2026-11-22). Its check-up is laid out by mod, so Vintage-E's card and
  the button for the EMC pages are under a "Vintage-E <version>" heading, and it runs every check each time (no
  "deeper" round). It writes out symbols the game's font may lack in whatever a mod hands it, so a report's marks
  and arrows can stay as they are. Nothing in the protocol changed: 1.2.0 still works with the bridge.
