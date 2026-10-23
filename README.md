# garmin-mcp

A local [MCP](https://modelcontextprotocol.io) server for your **Garmin Connect** data. It downloads your data with the
official [Personal API](https://developer.garmin.com/gc-developer-program/overview/), saves it in **Postgres**, and gives
it to Claude (or another MCP client) as tools that return JSON.

Why? Many third-party services import only the last year of Garmin data. This project keeps your full history of
workouts and health data (for example, since 2021). Claude can then use it to analyse your training or write notes
(for example, in Obsidian).

> **Status:** the code is complete and has 174 tests, but it is **not tested with a real Garmin account yet**.
> The field mapping is based on Garmin's OpenAPI specification. See [Known limitations](#known-limitations).

## How it works

```
Claude ──MCP (HTTP)──▶ garmin-mcp (.NET 10) ──▶ Postgres
                           │
                           └── only when you ask (sync tool) ──▶ Garmin Personal API
```

- **Sync only when you ask.** Claude calls `sync`. The job runs in the background, and `sync_status` shows the
  progress. The server does not connect to Garmin when it starts.
- **Incremental and resumable.** It downloads only what is missing. If it stops, the next `sync` continues from the
  same place. Health data from the last 7 days is always downloaded again, because Garmin often updates it later.
  `sync` with a date range downloads that range again.
- **Nothing is lost.** Every table has typed columns (for filtering) and a `raw` column (`jsonb`) with the full API
  response. FIT files are saved and also unpacked into per-second records and laps.
- **Careful with tokens.** The access token is saved in the database and used for 24 hours. One sync job asks for a
  new token at most once.
- **Reads come from the database.** The read tools never call Garmin, so they are fast and do not use API limits.

What is synced: activities (summary, details, workouts, original file, records and laps from FIT), daily summaries,
sleep, HRV, Body Battery, stress, steps, hydration, weight, daily heart rate, SpO2, wellness activities, nutrition,
menstrual cycle, heart rate zones, profile, fitness age and weight goals. Training load, intensity and time in zones
are Garmin's own values (nothing is recalculated).

## Requirements

- Docker with Docker Compose
- a Personal API key — you can create it yourself on the
  [Personal API Key](https://connect.garmin.com/app/settings/personalApiToken) page in Garmin Connect
- Claude Code (or another MCP client that supports the HTTP transport)

## Quick start

1. **Key** — save it in a file that only you can read:

   ```bash
   mkdir -p ~/.config/garmin-mcp
   install -m 600 /dev/null ~/.config/garmin-mcp/api-key
   $EDITOR ~/.config/garmin-mcp/api-key   # paste only the key
   ```

2. **Configuration** — copy `.env.example` and set the database password:

   ```bash
   cp .env.example .env
   # POSTGRES_PASSWORD=$(openssl rand -hex 16)
   ```

3. **Start:**

   ```bash
   docker compose up -d --build
   curl http://127.0.0.1:8765/healthz   # → ok
   ```

4. **Connect to Claude Code:**

   ```bash
   claude mcp add --transport http garmin http://127.0.0.1:8765/mcp
   ```

5. **First sync** — start with a short range to check the data, for example "sync the last 7 days". Then sync the
   full history ("sync everything"). A full sync sends a few thousand requests and takes about 1–3 hours.

## Configuration

Variables in `.env`:

| Variable | Default | Description |
|---|---|---|
| `POSTGRES_PASSWORD` | — (required) | Database password. Use only `[a-z0-9]` characters (for example hex), because it goes into the connection string. |
| `GARMIN_API_KEY_FILE` | `~/.config/garmin-mcp/api-key` | Path to the key file (mounted as a Docker secret). |
| `POSTGRES_PORT` | `5432` | Postgres port on the host (only on `127.0.0.1`). |
| `APP_UID` / `APP_GID` | `1000` | UID/GID of the owner of the key file. The container runs as this user, so it can read a file with mode 600 (check with `id -u`, `id -g`). |

App settings (section `Garmin`, for example as the variable `Garmin__RequestDelay`): `RequestDelay` (pause between
requests, default `00:00:00.5`), `MaxRetries` (3), `RetryBaseDelay` (`00:00:02`), `TailRefreshDays` (7).

## MCP tools

| Tool | Parameters | Returns |
|---|---|---|
| `sync` | `from?`, `to?` | Starts a background sync and returns immediately |
| `sync_status` | — | Status, phase, progress, errors, last successful sync |
| `list_activities` | `from?`, `to?`, `sport?`, `min_distance_km?`, `max_distance_km?`, `include_deleted`, `limit` (≤500), `offset` | List of activities (basic fields) |
| `get_activity` | `activity_id`, `include_raw` | Details, laps, time in HR zones, workouts |
| `get_activity_timeseries` | `activity_id`, `resolution` (`10s`/`30s`/`60s`/`km`/`lap`) | Averaged data over time: HR, pace, cadence, altitude, power (max. 500 points) |
| `get_sleep`, `get_hrv`, `get_body_battery`, `get_stress`, `get_daily_summary`, `get_weight` | `from`, `to` (max. 366 days) | Daily data |
| `get_heart_rate_zones`, `get_profile` | — | Latest saved heart rate zones / profile |

Responses are JSON in snake_case. Dates use `YYYY-MM-DD`, ranges include both ends, and UTC times end with `Z`.
If there is no data, the response has a message that suggests running `sync`. Units are described in the tool
descriptions (running and walking cadence is in steps per minute).

## Security

- Ports are published only on `127.0.0.1`. The MCP server has **no authentication**, so do not expose it outside
  localhost.
- Protection against DNS rebinding: only the `Host` header `localhost`/`127.0.0.1` is accepted, and requests to
  `/mcp` with an `Origin` header that is not loopback get 403.
- The key is read from a file (Docker secret). The key and the token never appear in logs, errors or tool responses.

## Known limitations

- **Not tested with a real account yet.** The field mapping is based on Garmin's OpenAPI specification
  ([Personal API Swagger Docs](https://developer.garmin.com/gc-developer-program/personal/swagger-docs/)). To check
  the API with your own account, run `python3 tools/api_probe.py` (it saves responses to `samples/`, which git
  ignores). If a response has an unexpected shape, the data chunk is not marked as downloaded and the problem appears
  in the `sync_status` errors, so data is not lost silently.
- **Token limit.** Garmin sometimes refuses to issue new tokens (401) even when the key is active. This limit is not
  documented. In this case the sync ends with the status `auth_failed`. Wait and try again later, or create a new key
  and replace the file.
- The units of muscle mass and bone mass (kg or g) still need to be checked with real data.

## Backup

```bash
docker compose exec -T postgres pg_dump -U garmin garmin > garmin-backup.sql
```

The data is stored in the `pgdata` volume. `docker compose down` keeps it; `docker compose down -v` deletes it.

## Development

You need the .NET 10 SDK and Docker. The tests use Testcontainers, and no test connects to Garmin.

```bash
dotnet test
```

Project structure:

```
src/GarminMcp/
  Garmin/    HTTP client, token, date range chunks
  Storage/   EF Core (Postgres), entities, migrations, record writer (COPY)
  Mapping/   API responses → entities
  Fit/       FIT file parser (Garmin FIT SDK)
  Sync/      activity and health data sync, background job
  Tools/     MCP tools
  Hosting/   Origin check
tests/GarminMcp.Tests/
tools/api_probe.py   checks the Personal API with your own account
```

Database migrations run automatically when the app starts.
