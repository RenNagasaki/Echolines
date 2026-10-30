# Echolines

Echolines collects FINAL FANTASY XIV dialogue lines reported by Echokraut installations and
keeps them in one place, in all four game languages.

Echokraut extracts dialogue from the game's own data files, but that extraction is never quite
complete: speakers that cannot be resolved, content added by a patch, sheet families nobody has
looked at yet. Echolines closes that gap from the other end — every line a player actually sees
is reported once, and what the extraction does not know shows up here.

Three things are sent per line: the text, its language, and which NPC speaks it. The player's
character name is replaced by a placeholder before sending. Nothing about the player is
transmitted or stored: no character name, no world, no account, no play time, no file paths.
Reporting is on by default and can be switched off in Echokraut under Settings → General.

New lines land in a staging table first and are only promoted once three separate installations
have reported them, so a single source cannot inject anything.

## API

Both endpoints are anonymous, always answer `200`, and are rate limited. `400` means the payload was
broken or oversized, `429` means slow down — the client ignores both and tries again later. Neither
ever surfaces as a message in game.

### `POST /v1/lines` — the live path

A player saw a dialogue line. The client buffers and sends roughly every 60 seconds or every 50 lines.

```jsonc
{
  "installId": "<guid>",          // random per installation, derived from nothing
  "pluginVersion": "0.19.3.1",
  "origin": "live",
  "lines": [
    { "lang": "de", "npc": "Alphinaud Leveilleur", "gender": "Male",
      "race": "Elezen", "npcBaseId": 1000123,
      "text": "Nun denn, <PLAYERFIRSTNAME>, ...",  // placeholder, NEVER the real name
      "hash": "<slug>",
      "source": "AddonTalk" }
  ]
}
```

### `POST /v1/lines/import` — the bulk path

A plugin-side harvest submits one language's entire result, roughly 288 000 lines in chunks of up to
1000. Same rules as the live path, its own rate budget, and without exception into quarantine: one
sender cannot push anything public no matter how much it submits.

```jsonc
{
  "installId": "<guid>",
  "pluginVersion": "0.19.3.1",
  "origin": "harvest",
  "chunk": 7, "chunkCount": 288,   // for the client's own resumability; the server may ignore them
  "lines": [ /* … up to 1000 … */ ]
}
```

Notes for client authors:

- **Send `X-Install-Id` as a header as well as in the body.** The rate limiter has to partition
  before the body is read; without the header a request is limited by IP alone, which lumps
  everyone behind one NAT together.
- **The `hash` field is accepted but not trusted.** The server recomputes it from `text` and uses its
  own value as the key. Send it anyway — a mismatch is logged and is a useful signal that the two
  sides have drifted.
- **The text must already be tokenised.** Replace the character name before sending, not after.
- **Narrator lines are welcome and need no special handling.** Quest text with no speaker — the
  SYSTEM, QIB and POPMESSAGE entries — is a large part of the game's dialogue. Send `npc` empty, or
  send `SYSTEM`, or send the localised display name the harvest produces (`Narrator`, `Erzähler`,
  `Narrateur`, `ナレーター`): the server folds all of them onto `SYSTEM`. That folding matters,
  because the live path sends an empty speaker and the harvest sends a display name for the very same
  line — kept apart, one line would sit in the database twice and neither copy would reach three votes.

## Admin UI

`/admin`, behind a single operator account. Three jobs:

- **Quarantine** — release or turn down entries by hand, below the three-installation threshold. This
  matters most for the rarer languages, where three harvesting installations may never turn up.
- **Insights** — "seen live but never harvested" is the harvest gap, and the point of the whole build.
- **Drive import** — reads the old "one JSON file per line" Google Drive stock into the main table.

The password is a PBKDF2 hash in `Admin__PasswordHash`. Generate one with:

```
docker build -t echolines .
docker run --rm -it echolines hash-password
```

An empty hash refuses every login rather than allowing every login.

## Running it

There is no published image: compose builds it from this repository. Clone it, copy `.env.example`
to `.env` and fill it in.

```
git clone https://github.com/RenNagasaki/Echolines.git
cd Echolines
cp .env.example .env

# The operator password hash. Generate it with a plain `docker run`, not through compose:
# the compose service declares ADMIN_PASSWORD_HASH as required, so it refuses to start any
# command — including this one — until the value it is meant to produce already exists.
docker compose build server
docker run --rm -it echolines hash-password

# paste the resulting pbkdf2$… string into ADMIN_PASSWORD_HASH, set POSTGRES_PASSWORD, then:
docker compose up -d
```

The database schema is created and migrated on startup; there is no separate setup step. The admin
UI is at `/admin`, the health probe at `/healthz`.

### Configuration

Every setting is an environment variable on the `server` container. Double underscore is the section
separator — `Admin__PasswordHash` is the `Admin:PasswordHash` setting.

| Variable | Default | What it does |
|---|---|---|
| `ConnectionStrings__Default` | — | **Required.** Npgsql connection string. |
| `Admin__PasswordHash` | empty | **Required in practice.** PBKDF2 hash from `hash-password`. Empty refuses every login. |
| `Admin__SessionLifetime` | `7.00:00:00` | How long a signed-in operator stays signed in (`d.hh:mm:ss`). |
| `Admin__LoginAttemptsPerMinute` | `5` | Login attempts per minute per IP. The only attack a single account has is brute force. |
| `Server__BehindReverseProxy` | `false` | Honour `X-Forwarded-For`/`-Proto`. **Set this when TLS is terminated in front** — see below. |
| `Server__ForwardLimit` | `1` | How many proxies deep to trust. Raise only if you really have a chain. |
| `Ingest__PromotionThreshold` | `3` | Distinct installations before a quarantined line becomes public. |
| `Ingest__MaxLinesPerLiveRequest` | `200` | Batch cap on `/v1/lines`; above it, `400`. |
| `Ingest__MaxLinesPerImportRequest` | `1000` | Chunk cap on `/v1/lines/import`. |
| `RateLimit__LivePermitPerMinute` | `20` | Requests per minute per installation and IP on the live path. |
| `RateLimit__ImportPermitPerMinute` | `6` | Same for the bulk path. Deliberately low: a harvest should take hours, not minutes. |
| `ASPNETCORE_HTTP_PORTS` | `8080` | Port inside the container. |

Data lives in the `echolines-data` volume. That volume is the deployment — the container itself holds
nothing worth keeping.

### Behind a reverse proxy

**Set `Server__BehindReverseProxy=true` whenever something terminates TLS in front of the container**,
and bind the published port to `127.0.0.1` so the plain-HTTP port is not reachable from outside.
Without it two things fail quietly rather than loudly:

- Every client shares a single rate-limit bucket, because they all arrive from the proxy's address.
- The admin session cookie never gets its `Secure` flag, because the request reaches the app as HTTP.

Conversely, do **not** set it when the port is exposed directly: the app would then believe an
`X-Forwarded-For` header any caller can write, which hands them a way around the rate limit.

The service speaks plain HTTP by design and holds no certificates. TLS belongs to whatever sits in
front.

**The proxy must forward WebSockets.** The admin UI is Blazor Server: every control on it — the
checkboxes, the release and turn-down buttons, the filters — works through a WebSocket connection to
`/_blazor`. A proxy that drops the `Upgrade`/`Connection` headers leaves the pages rendering perfectly
and reacting to nothing at all, which reads like a broken page rather than a broken proxy. The
reporting endpoints under `/v1` are unaffected; they are plain HTTP and keep working either way.

nginx:

```nginx
location / {
    proxy_pass http://127.0.0.1:8080;
    proxy_http_version 1.1;
    proxy_set_header Upgrade    $http_upgrade;    # these two lines are the WebSocket part
    proxy_set_header Connection $connection_upgrade;
    proxy_set_header Host              $host;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_read_timeout 100s;   # the circuit is long-lived; a short timeout drops it mid-session
}
```

Traefik and Caddy handle the upgrade on their own and need nothing extra. In a Kubernetes ingress,
check the controller's own WebSocket setting — several disable it by default.

To confirm it works, open the admin UI and look at the browser's network tab, filtered to `WS`: there
must be a connection to `/_blazor` sitting at `101 Switching Protocols`. If the circuit cannot be
established the page shows a bar along the bottom saying the connection was lost.

## Development

```
node .claude/verify.js    # build + tests, exits 0 when green
```

`Echolines.Tests` runs against in-memory SQLite rather than the InMemory provider, because
`UNIQUE(pending_id, voter_key)` *is* the consensus rule and the InMemory provider ignores unique
indexes entirely.

## License

[GNU Affero General Public License v3.0](LICENSE.md), the same as Echokraut and the other Echotools
projects. AGPL rather than GPL is the right fit here for a reason worth stating: this is server
software people reach over a network, and section 13 is what keeps a hosted copy from becoming a
closed fork of the community's own data pipeline.
