# garmin-mcp

Lokalny serwer [MCP](https://modelcontextprotocol.io), który pobiera Twoje dane z **Garmin Connect** przez oficjalne
[Personal API](https://developer.garmin.com/gc-developer-program/overview/), trzyma je w **Postgresie** i udostępnia
Claude'owi (lub innemu klientowi MCP) jako gotowe narzędzia zwracające JSON.

Powstał po to, żeby mieć pod ręką pełną historię treningów i danych zdrowotnych (np. od 2021 roku), a nie tylko
ostatni rok, który importują serwisy trzecie, i żeby Claude mógł na ich podstawie analizować treningi
czy pisać notatki (np. do Obsidiana).

> **Status:** kod jest kompletny i pokryty testami (174), ale **nie został jeszcze zweryfikowany na prawdziwym
> koncie Garmin** — mapowanie odpowiedzi API powstało na podstawie specyfikacji OpenAPI Garmina.
> Zobacz [Znane ograniczenia](#znane-ograniczenia).

## Jak to działa

```
Claude ──MCP (HTTP)──▶ garmin-mcp (.NET 10) ──▶ Postgres
                           │
                           └── tylko na żądanie (narzędzie sync) ──▶ Garmin Personal API
```

- **Synchronizacja tylko na żądanie** — Claude wywołuje `sync`, zadanie biegnie w tle, postęp widać w `sync_status`.
  Start serwera nie łączy się z Garminem.
- **Wznawialna i przyrostowa** — pobiera tylko to, czego brakuje; po przerwaniu kontynuuje. Dane zdrowotne z
  ostatnich 7 dni są zawsze odświeżane (Garmin uzupełnia je z opóźnieniem). `sync` z zakresem dat wymusza
  ponowne pobranie tego zakresu.
- **Nic nie ginie** — każda tabela ma typowane kolumny (do filtrowania) i kolumnę `raw` (`jsonb`) z pełną
  odpowiedzią API. Pliki FIT są przechowywane i rozpakowywane do rekordów sekunda po sekundzie oraz okrążeń.
- **Oszczędne z tokenami** — token jest zapisywany w bazie i używany przez 24 h; jedno zadanie synchronizacji
  prosi o nowy token najwyżej raz.
- **Odczyt z bazy** — narzędzia do odczytu nie odpytują Garmina, więc są szybkie i nie zużywają limitów.

Synchronizowane dane: aktywności (podsumowanie, szczegóły, workouts, oryginalny plik, rekordy i okrążenia z FIT),
podsumowania dzienne, sen, HRV, Body Battery, stres, kroki, nawodnienie, waga, tętno dzienne, SpO2, aktywności
wellness, odżywianie, cykl menstruacyjny, strefy tętna, profil, fitness age, cele wagowe. Load, intensity i czas w
strefach to natywne wartości Garmina (bez przeliczania).

## Wymagania

- Docker z Docker Compose,
- klucz Personal API — generujesz go sam na stronie
  [Personal API Key](https://connect.garmin.com/app/settings/personalApiToken) w Garmin Connect,
- Claude Code (lub inny klient MCP obsługujący transport HTTP).

## Szybki start

1. **Klucz** — zapisz go w pliku dostępnym tylko dla Ciebie:

   ```bash
   mkdir -p ~/.config/garmin-mcp
   install -m 600 /dev/null ~/.config/garmin-mcp/api-key
   $EDITOR ~/.config/garmin-mcp/api-key   # wklej sam klucz
   ```

2. **Konfiguracja** — skopiuj `.env.example` i ustaw hasło bazy:

   ```bash
   cp .env.example .env
   # POSTGRES_PASSWORD=$(openssl rand -hex 16)
   ```

3. **Start:**

   ```bash
   docker compose up -d --build
   curl http://127.0.0.1:8765/healthz   # → ok
   ```

4. **Podłączenie do Claude Code:**

   ```bash
   claude mcp add --transport http garmin http://127.0.0.1:8765/mcp
   ```

5. **Pierwsza synchronizacja** — najpierw krótki zakres, żeby sprawdzić dane, np. „zsynchronizuj ostatnie 7 dni”,
   potem pełna historia („zsynchronizuj wszystko”). Pełna synchronizacja to kilka tysięcy zapytań i trwa ok. 1–3 h.

## Konfiguracja

Zmienne w `.env`:

| Zmienna | Domyślnie | Opis |
|---|---|---|
| `POSTGRES_PASSWORD` | — (wymagana) | Hasło bazy. Używaj znaków `[a-z0-9]` (np. hex), bo trafia do connection stringa. |
| `GARMIN_API_KEY_FILE` | `~/.config/garmin-mcp/api-key` | Ścieżka do pliku z kluczem (montowany jako docker secret). |
| `POSTGRES_PORT` | `5432` | Port Postgresa na hoście (tylko `127.0.0.1`). |
| `APP_UID` / `APP_GID` | `1000` | UID/GID właściciela pliku klucza — kontener działa jako ten użytkownik, żeby odczytać plik z prawami 600 (`id -u`, `id -g`). |

Ustawienia aplikacji (sekcja `Garmin`, np. jako zmienne `Garmin__RequestDelay`): `RequestDelay` (odstęp między
zapytaniami, domyślnie `00:00:00.5`), `MaxRetries` (3), `RetryBaseDelay` (`00:00:02`), `TailRefreshDays` (7).

## Narzędzia MCP

| Narzędzie | Parametry | Zwraca |
|---|---|---|
| `sync` | `from?`, `to?` | Startuje synchronizację w tle i od razu wraca |
| `sync_status` | — | Stan, faza, postęp, błędy, ostatnia udana synchronizacja |
| `list_activities` | `from?`, `to?`, `sport?`, `min_distance_km?`, `max_distance_km?`, `include_deleted`, `limit` (≤500), `offset` | Lista aktywności (podstawowe pola) |
| `get_activity` | `activity_id`, `include_raw` | Szczegóły, okrążenia, czas w strefach HR, workouts |
| `get_activity_timeseries` | `activity_id`, `resolution` (`10s`/`30s`/`60s`/`km`/`lap`) | Uśredniony przebieg: HR, tempo, kadencja, wysokość, moc (maks. 500 punktów) |
| `get_sleep`, `get_hrv`, `get_body_battery`, `get_stress`, `get_daily_summary`, `get_weight` | `from`, `to` (maks. 366 dni) | Dane dzienne |
| `get_heart_rate_zones`, `get_profile` | — | Najnowszy zapis stref tętna / profilu |

Odpowiedzi to JSON w snake_case; daty `YYYY-MM-DD`, zakresy włącznie, czasy UTC z `Z`. Gdy brak danych,
odpowiedź zawiera komunikat z sugestią uruchomienia `sync`. Jednostki są opisane w opisach narzędzi
(kadencja biegu/marszu w krokach na minutę).

## Bezpieczeństwo

- Porty są publikowane wyłącznie na `127.0.0.1`. Serwer MCP **nie ma uwierzytelniania** — nie wystawiaj go poza
  localhost.
- Ochrona przed DNS rebinding: akceptowany jest tylko nagłówek `Host` `localhost`/`127.0.0.1`, a żądania do `/mcp`
  z nagłówkiem `Origin` spoza loopback dostają 403.
- Klucz jest czytany z pliku (docker secret); klucz ani token nie trafiają do logów, wyjątków ani odpowiedzi narzędzi.

## Znane ograniczenia

- **Brak weryfikacji na prawdziwym koncie.** Mapowanie pól powstało ze specyfikacji OpenAPI Garmina
  ([docs/garmin-personal-api-openapi.json](docs/garmin-personal-api-openapi.json)). Do sprawdzenia API na swoim koncie
  służy `python3 tools/api_probe.py` (zapisuje odpowiedzi do ignorowanego przez git katalogu `samples/`).
  Jeśli odpowiedź ma inny kształt niż oczekiwany, paczka nie jest oznaczana jako pobrana i trafia do błędów w
  `sync_status` — dane nie giną po cichu.
- **Limit wydawania tokenów.** Garmin potrafi odmawiać nowych tokenów (401) mimo aktywnego klucza — limit nie jest
  udokumentowany. Synchronizacja kończy się wtedy statusem `auth_failed`; odczekaj albo wygeneruj nowy klucz i
  podmień plik.
- Jednostki masy mięśniowej i kostnej (kg vs g) wymagają potwierdzenia na prawdziwych danych.

## Backup

```bash
docker compose exec -T postgres pg_dump -U garmin garmin > garmin-backup.sql
```

Dane leżą w wolumenie `pgdata` (`docker compose down` go nie usuwa, `down -v` — tak).

## Rozwój

Wymaga .NET 10 SDK i Dockera (testy używają Testcontainers; żaden test nie łączy się z Garminem).

```bash
dotnet test
```

Struktura:

```
src/GarminMcp/
  Garmin/    klient HTTP, token, dzielenie zakresów dat
  Storage/   EF Core (Postgres), encje, migracje, zapis rekordów (COPY)
  Mapping/   odpowiedzi API → encje
  Fit/       parser plików FIT (Garmin FIT SDK)
  Sync/      synchronizacja aktywności i danych zdrowotnych, zadanie w tle
  Tools/     narzędzia MCP
  Hosting/   walidacja Origin
tests/GarminMcp.Tests/
docs/        specyfikacja API Garmina, dokument projektowy i plan implementacji
```

Migracje bazy uruchamiają się automatycznie przy starcie aplikacji.
