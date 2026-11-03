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
