# garmin-mcp

Lokalny serwer MCP (C# / .NET 10), który synchronizuje dane z Garmin Connect (Personal API) do Postgresa
i udostępnia je Claude'owi jako narzędzia MCP. Synchronizacja działa wyłącznie na żądanie (narzędzie `sync`);
start serwera nie łączy się z Garminem.

## Wymagania

- Docker z Docker Compose,
- klucz Personal API Garmin (przyznawany przez Garmin Connect Developer Program),
- Claude Code (lub inny klient MCP z transportem HTTP).

## Uruchomienie

1. Zapisz klucz w pliku dostępnym tylko dla Ciebie:

   ```bash
   mkdir -p ~/.config/garmin-mcp
   install -m 600 /dev/null ~/.config/garmin-mcp/api-key
   $EDITOR ~/.config/garmin-mcp/api-key   # wklej sam klucz
   ```

   Inna lokalizacja: zmienna `GARMIN_API_KEY_FILE` w `.env`. Kontener działa jako UID/GID użytkownika hosta
   (domyślnie 1000:1000; zmiana przez `APP_UID`/`APP_GID` w `.env`), żeby odczytać plik z prawami 600.

2. Skonfiguruj hasło bazy:

   ```bash
   cp .env.example .env
   # ustaw POSTGRES_PASSWORD (np. openssl rand -hex 16)
   ```

3. Uruchom:

   ```bash
   docker compose up -d --build
   curl http://127.0.0.1:8765/healthz   # ok
   ```

   Porty są publikowane tylko na `127.0.0.1` (MCP: 8765, Postgres: 5432, zmienialny przez `POSTGRES_PORT`).
   Serwer MCP nie ma uwierzytelniania, więc nie wystawiaj go poza localhost.

4. Podłącz do Claude Code:

   ```bash
   claude mcp add --transport http garmin http://127.0.0.1:8765/mcp
   ```

## Narzędzia

| Narzędzie | Opis |
|---|---|
| `sync` | Uruchamia synchronizację w tle (zakres dat opcjonalny) |
| `sync_status` | Postęp i wynik synchronizacji |
| `list_activities` | Lista aktywności z zakresu dat |
| `get_activity` | Szczegóły jednej aktywności |
| `get_activity_timeseries` | Szereg czasowy aktywności (z agregacją, np. `resolution = "km"`) |
| `get_sleep` | Sen |
| `get_hrv` | Zmienność rytmu serca (HRV) |
| `get_body_battery` | Body Battery |
| `get_stress` | Stres |
| `get_daily_summary` | Dzienne podsumowanie |
| `get_weight` | Waga i skład ciała |
| `get_heart_rate_zones` | Strefy tętna |
| `get_profile` | Profil użytkownika |

Odpowiedzi to JSON w snake_case; daty `YYYY-MM-DD`, zakresy włącznie. Gdy brak danych, odpowiedź zawiera
komunikat z prośbą o uruchomienie `sync`.

## Uwagi

- **Pierwsza synchronizacja** pobiera całą historię: tysiące zapytań, przy `Garmin__RequestDelay` 500 ms
  zajmuje ok. 1-3 godzin. Postęp sprawdzisz przez `sync_status`. Synchronizacja jest wznawialna: po
  przerwaniu (np. restarcie kontenera) kolejne `sync` kontynuuje od miejsca przerwania. Dla szybkiego testu
  uruchom `sync` z zakresem ostatnich kilku dni.
- **Limit tokenów / 401**: token jest wymieniany z klucza i cache'owany w bazie. Jeśli Garmin zwraca 401
  przy wymianie tokenu, odczekaj (limit wymian) i spróbuj ponownie; jeśli nie pomaga, wygeneruj nowy klucz
  i podmień plik.
- Klucz i tokeny nie trafiają do logów ani odpowiedzi narzędzi.

## Backup

```bash
docker compose exec -T postgres pg_dump -U garmin garmin > garmin-backup.sql
```

Dane leżą w wolumenie `pgdata` (`docker compose down` go nie usuwa; `down -v` tak).

## Rozwój

```bash
dotnet test   # testy używają Testcontainers (wymagany Docker); nie łączą się z Garminem
```
