# Echolines

ASP.NET Core server (net10.0) behind Echokraut's community line database. The plan it implements is
`central-line-database.md` in this repo — read that first for the *why*; this file covers the *what*.

## Build, test, verify

- **One check command: `node .claude/verify.js`** (build + tests, `exit 0` when green).
- The system `dotnet` is SDK 9 and cannot target net10.0. Always use `~/.dotnet/dotnet.exe`.
  For `dotnet-ef`, put `~/.dotnet` **first** on `PATH` — the tool resolves whichever `dotnet` it finds.
- **`NuGet.config` in the repo root is load-bearing.** The machine has a private company feed
  configured globally that answers `401`; the file clears the source list down to nuget.org. Restore
  fails without it, in the container too.
- **SonarQube project key: `echotools_echolines_752836cc-9f0e-42e8-a523-2af591d96d44`.**
  **Hardcoded in `.gitlab-ci.yml`**, not a CI/CD variable — it is not a secret and never varies.
  Only `SONAR_HOST_URL` and `SONAR_TOKEN` come from the group-level variables.
  ⚠ **It is not the repo name.** Projects on this instance are provisioned by the GitLab integration,
  which generates `echotools_<repo>_<uuid>`; Echokraut's is `echotools_echokraut_9b9e9dc6-…`. Only the
  provisioned project carries the permissions the CI token holds — a hand-created project with a tidy
  name answers `403 Forbidden` to that token and the job dies in the pre-processing step. There is a
  stray hand-created `Echolines` project on the server from 2026-08-10 that should be deleted.
  Exclusion: `**/Migrations/**` — the migration files are generated, and their findings are noise
  nobody can act on without breaking the migration.
  Coverage exclusions: `**/*.razor,**/Program.cs,**/Migrations/**` — markup, host wiring and
  generated code. Every file that holds a decision stays measured (currently 88 %).
  **Run the scan from PowerShell, not Git Bash**: bash mangles `/key:` and `/d:` into paths and the
  scanner rejects them as unknown arguments. Coverage has to be collected *between* begin and end,
  or the quality gate fails on `new_coverage` alone:
  ```powershell
  $env:PATH = "C:\Users\jkatz\.dotnet;C:\Users\jkatz\.dotnet\tools;$env:PATH"
  dotnet-sonarscanner begin /key:echotools_echolines_752836cc-9f0e-42e8-a523-2af591d96d44 `
    /d:sonar.host.url=https://sonarqube.echotools.cloud `
    /d:sonar.token=$SONAR_TOKEN /d:sonar.exclusions=**/Migrations/** `
    /d:sonar.coverage.exclusions="**/*.razor,**/Program.cs,**/Migrations/**" `
    /d:sonar.cs.opencover.reportsPaths=TestResults/**/coverage.opencover.xml
  dotnet build Echolines.sln
  dotnet test Echolines.sln --no-build --collect:"XPlat Code Coverage;Format=opencover" --results-directory TestResults
  dotnet-sonarscanner end /d:sonar.token=$SONAR_TOKEN
  ```

## Layout

```
Echolines.Server/
  Program.cs              host wiring; also the `hash-password` one-shot command
  Data/                   EcholinesDbContext, LineLimits, Entities/{Line,PendingLine,PendingVote}
  Models/SubmitRequest    the wire format of both endpoints
  Services/               LineKey, LineIngestService, LinePromotion, LineReviewService,
                          DriveImportService, RateLimitPolicies, IngestOptions
  Endpoints/              POST /v1/lines, POST /v1/lines/import
  Admin/                  AdminOptions, AdminPasswordHasher, AdminEndpoints (login/logout)
  Components/             Blazor Server admin UI (Quarantine, Insights, DriveImport, Login)
                          Quarantine "Release all N": two-step confirm, releases the whole
                          current filter via LineReviewService.ApproveAllAsync
Echolines.Tests/          xUnit, in-memory SQLite
```

## The six things that are easy to get wrong

**-1. The Dockerfile must not publish with `--no-restore`.** `blazor.web.js` is not part of the SDK:
it ships in the `Microsoft.AspNetCore.App.Internal.Assets` NuGet package. The restore that precedes
`COPY . .` runs when only the `.csproj` files exist, and a project with no `.razor` files in it is
not a Blazor project as far as the SDK is concerned — so that package never enters the dependency
graph. `--no-restore` on the publish then means it never can, and the image ships a `wwwroot` with no
`_framework` in it. Nothing warns; the admin UI renders perfectly and responds to nothing, because
the script that starts the circuit answers 404. It cannot be reproduced on a machine whose NuGet
cache already holds the package, which is every developer machine — only a cold cache shows it.
The Dockerfile asserts the file exists after publish, so this fails the build rather than the user.


**0. A form must never post to its own Razor page route.** Since .NET 8 a component route answers
`POST` as well — that is how static form handling works — so a minimal-API handler mapped onto the
same path is a second, equally specific match and routing throws `AmbiguousMatchException`. The
sign-in form posts to `AdminEndpoints.SignInPath` (`/admin/signin`), never to the page at
`/admin/login`. This shipped broken once: every sign-in answered `500`, right password or wrong,
while the whole suite stayed green because no test routed a POST. `AdminLoginTests` does now.


**1. `LineKey` must not use `Path.GetInvalidFileNameChars()`.** The key is the same string as
Echokraut's WAV file name and `*.ekpack` entry, produced by `TalkTextHelper.VoiceMessageToFileName`.
That plugin function calls the framework API, which returns 41 characters on Windows and two on
Linux. The plugin runs on Windows; this server runs in a Linux container. The Windows set is
hardcoded in `LineKey`, and `LineKeyTests` guards it. A colon is common in FFXIV dialogue, so getting
this wrong would silently split real lines into two entries.

**2. "Already known ⇒ do nothing" is wrong.** A line promoted by three harvests would keep
`SeenLive = false` forever even after players met it, and the Insights page would report a harvest gap
that closed months ago. `LineIngestService.UpdateOrigin` catches the flags up on a line that already
exists. `OriginTests` covers both directions.

**3. The normal path must stay a pure read.** Almost every reported line is one everybody already
knows. `last_seen_utc` is therefore only advanced once `IngestOptions.LastSeenResolution` (a day) has
passed, so an unchanged report writes nothing at all.

**4. Nothing is ever deleted from `pending_lines`.** Entries that never reach three votes are the
interesting material. `state` (`pending | rejected | promoted`) is the only way an entry leaves the
work list — a deleted row is simply re-created by the next client that reports the line. The table
therefore outgrows `lines`, which is why every view is paginated and indexed from day one.

## Design decisions worth not re-deriving

- **The `hash` field is recomputed server-side, not trusted.** It is a deterministic slug, not a
  digest, so the server can produce it from the text. A client sending a wrong hash would otherwise
  split one line into arbitrarily many keys. The client's value is compared and a mismatch counted as
  a health signal.
- **Origin is decided by the endpoint, never by the payload's `origin` field.** A client claiming
  `harvest` on the live endpoint would corrupt the one distinction the Insights page rests on.
- **`installId` is only accepted as a non-empty GUID.** Free text there would turn the vote key into a
  place a character name could land.
- **Rate limiting partitions on the `X-Install-Id` header**, because the limiter runs before the body
  is read. Falls back to IP alone. Client authors must send the header as well as the body field.
- **Tests use SQLite, not the InMemory provider.** `UNIQUE(pending_id, voter_key)` *is* the consensus
  rule and InMemory ignores unique indexes — the suite would certify the bug it exists to catch.
- **`Database:Provider=none`** lets a host register its own EF provider; the test factory uses it,
  because two providers in one service collection is an error EF will not guess its way out of.
- **An empty speaker is the narrator, not a broken report.** Echokraut's live path sends `""` for
  quest text with no speaker (`AddonTalkHelper`: `state.Speaker ?? ""`), while the harvest labels the
  same lines with a localised display name under its internal key `SYSTEM`. `NpcName.Canonicalise`
  folds `""`, `SYSTEM` and the four localised names onto `SYSTEM`, so live and harvest votes land on
  one entry. Rejecting the empty name — which is what the first version did — threw away most of the
  quest dialogue in the game, on both endpoints.
- **An empty `Admin:PasswordHash` refuses every login**, never allows every login.
- **`Server:BehindReverseProxy` is opt-in and matters more than it looks.** Off, `X-Forwarded-For` is
  ignored — correct for a directly exposed port, where honouring it would let a caller invent an
  address per request and walk around the rate limit. On, the forwarded address is used — required
  behind a TLS-terminating proxy, or every client shares one rate-limit bucket and the admin cookie
  never gets its `Secure` flag (`CookieSecurePolicy.SameAsRequest` sees plain HTTP). Both failure
  modes are silent; `ReverseProxyTests` pins both directions.
- **"Release all" works in id-keyset batches of 1000** (`ApproveAllAsync`), clearing the change
  tracker after each. The admin DbContext lives as long as the Blazor circuit, so one big load would
  keep a six-figure quarantine tracked; "take the first N still pending" instead of a keyset would
  loop forever on an entry that does not move. Page view and release-all share one `Matching`
  filter so the confirmed count and the released set cannot disagree. Pending state only, always.

## State of the plan

Implemented here: phases 0, 1, 2, 4, 5, 6 of `central-line-database.md`.

**Phases 3, 3b and 7 are client-side and live in the Echokraut repo**, not here:

- **3** — `ILineSubmissionService`, local suppression set, batching, `installId`, the opt-out switch in
  both the first-time wizard and Settings → General, and the changelog entry.
- **3b** — the harvest triggering the bulk import, chunked, resumable per language.
- **7** — removing the dead `UploadVoiceLine` / `DriveUploadService` path (`DriveLinkHelper` and the
  download side stay).

The call site for reporting is `VoiceMessageProcessor.cs:187` (step 3.5), **not** `LogVoiceClip` —
that sits behind four early returns, two of which fire in None mode.
