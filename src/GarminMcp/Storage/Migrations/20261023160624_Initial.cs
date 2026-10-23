using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GarminMcp.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "activities",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    sport = table.Column<string>(type: "text", nullable: true),
                    event_type = table.Column<string>(type: "text", nullable: true),
                    start_time_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    start_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    distance_m = table.Column<double>(type: "double precision", nullable: true),
                    duration_s = table.Column<double>(type: "double precision", nullable: true),
                    moving_duration_s = table.Column<double>(type: "double precision", nullable: true),
                    elapsed_duration_s = table.Column<double>(type: "double precision", nullable: true),
                    avg_hr = table.Column<double>(type: "double precision", nullable: true),
                    max_hr = table.Column<double>(type: "double precision", nullable: true),
                    avg_speed_mps = table.Column<double>(type: "double precision", nullable: true),
                    max_speed_mps = table.Column<double>(type: "double precision", nullable: true),
                    avg_cadence = table.Column<double>(type: "double precision", nullable: true),
                    elevation_gain_m = table.Column<double>(type: "double precision", nullable: true),
                    calories = table.Column<double>(type: "double precision", nullable: true),
                    avg_power_w = table.Column<double>(type: "double precision", nullable: true),
                    training_load = table.Column<double>(type: "double precision", nullable: true),
                    intensity_factor = table.Column<double>(type: "double precision", nullable: true),
                    aerobic_te = table.Column<double>(type: "double precision", nullable: true),
                    anaerobic_te = table.Column<double>(type: "double precision", nullable: true),
                    hr_zone1s = table.Column<double>(type: "double precision", nullable: true),
                    hr_zone2s = table.Column<double>(type: "double precision", nullable: true),
                    hr_zone3s = table.Column<double>(type: "double precision", nullable: true),
                    hr_zone4s = table.Column<double>(type: "double precision", nullable: true),
                    hr_zone5s = table.Column<double>(type: "double precision", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    raw_detail = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    raw_workouts = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    file_status = table.Column<string>(type: "text", nullable: false, defaultValue: "pending"),
                    list_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detail_fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activities", x => x.activity_id);
                });

            migrationBuilder.CreateTable(
                name: "activity_files",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    format = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_files", x => x.activity_id);
                });

            migrationBuilder.CreateTable(
                name: "activity_laps",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    lap_index = table.Column<int>(type: "integer", nullable: false),
                    start_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    elapsed_s = table.Column<double>(type: "double precision", nullable: true),
                    distance_m = table.Column<double>(type: "double precision", nullable: true),
                    avg_hr = table.Column<double>(type: "double precision", nullable: true),
                    max_hr = table.Column<double>(type: "double precision", nullable: true),
                    avg_speed_mps = table.Column<double>(type: "double precision", nullable: true),
                    avg_cadence = table.Column<double>(type: "double precision", nullable: true),
                    ascent_m = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_laps", x => new { x.activity_id, x.lap_index });
                });

            migrationBuilder.CreateTable(
                name: "activity_records",
                columns: table => new
                {
                    activity_id = table.Column<long>(type: "bigint", nullable: false),
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    elapsed_s = table.Column<int>(type: "integer", nullable: false),
                    hr = table.Column<short>(type: "smallint", nullable: true),
                    speed_mps = table.Column<float>(type: "real", nullable: true),
                    cadence = table.Column<short>(type: "smallint", nullable: true),
                    altitude_m = table.Column<float>(type: "real", nullable: true),
                    power_w = table.Column<short>(type: "smallint", nullable: true),
                    distance_m = table.Column<float>(type: "real", nullable: true),
                    lat = table.Column<double>(type: "double precision", nullable: true),
                    lon = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_activity_records", x => new { x.activity_id, x.timestamp_utc });
                });

            migrationBuilder.CreateTable(
                name: "auth_token",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth_token", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "body_battery",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    charged = table.Column<int>(type: "integer", nullable: true),
                    drained = table.Column<int>(type: "integer", nullable: true),
                    min = table.Column<int>(type: "integer", nullable: true),
                    max = table.Column<int>(type: "integer", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_body_battery", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "daily_summary",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    steps = table.Column<int>(type: "integer", nullable: true),
                    total_kcal = table.Column<double>(type: "double precision", nullable: true),
                    active_kcal = table.Column<double>(type: "double precision", nullable: true),
                    resting_hr = table.Column<int>(type: "integer", nullable: true),
                    min_hr = table.Column<int>(type: "integer", nullable: true),
                    max_hr = table.Column<int>(type: "integer", nullable: true),
                    avg_stress = table.Column<int>(type: "integer", nullable: true),
                    max_stress = table.Column<int>(type: "integer", nullable: true),
                    body_battery_min = table.Column<int>(type: "integer", nullable: true),
                    body_battery_max = table.Column<int>(type: "integer", nullable: true),
                    moderate_minutes = table.Column<int>(type: "integer", nullable: true),
                    vigorous_minutes = table.Column<int>(type: "integer", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_summary", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "health_chunks",
                columns: table => new
                {
                    dataset = table.Column<string>(type: "text", nullable: false),
                    chunk_start = table.Column<DateOnly>(type: "date", nullable: false),
                    chunk_end = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_health_chunks", x => new { x.dataset, x.chunk_start });
                });

            migrationBuilder.CreateTable(
                name: "heart_rate_daily",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    resting = table.Column<int>(type: "integer", nullable: true),
                    min = table.Column<int>(type: "integer", nullable: true),
                    max = table.Column<int>(type: "integer", nullable: true),
                    seven_day_avg_resting = table.Column<int>(type: "integer", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_heart_rate_daily", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "hrv",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    last_night_avg = table.Column<int>(type: "integer", nullable: true),
                    weekly_avg = table.Column<int>(type: "integer", nullable: true),
                    last_night5min_high = table.Column<int>(type: "integer", nullable: true),
                    baseline_low = table.Column<int>(type: "integer", nullable: true),
                    baseline_high = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hrv", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "hydration",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    value_ml = table.Column<double>(type: "double precision", nullable: true),
                    goal_ml = table.Column<double>(type: "double precision", nullable: true),
                    sweat_loss_ml = table.Column<double>(type: "double precision", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hydration", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "raw_daily",
                columns: table => new
                {
                    dataset = table.Column<string>(type: "text", nullable: false),
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_daily", x => new { x.dataset, x.calendar_date });
                });

            migrationBuilder.CreateTable(
                name: "sleep",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sleep_s = table.Column<int>(type: "integer", nullable: true),
                    deep_s = table.Column<int>(type: "integer", nullable: true),
                    light_s = table.Column<int>(type: "integer", nullable: true),
                    rem_s = table.Column<int>(type: "integer", nullable: true),
                    awake_s = table.Column<int>(type: "integer", nullable: true),
                    sleep_score = table.Column<int>(type: "integer", nullable: true),
                    avg_hr = table.Column<double>(type: "double precision", nullable: true),
                    avg_respiration = table.Column<double>(type: "double precision", nullable: true),
                    avg_sleep_stress = table.Column<double>(type: "double precision", nullable: true),
                    sleep_start_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    sleep_end_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sleep", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    dataset = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "steps",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    total_steps = table.Column<int>(type: "integer", nullable: true),
                    total_distance_m = table.Column<int>(type: "integer", nullable: true),
                    step_goal = table.Column<int>(type: "integer", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_steps", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "stress",
                columns: table => new
                {
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    overall = table.Column<int>(type: "integer", nullable: true),
                    high_s = table.Column<int>(type: "integer", nullable: true),
                    medium_s = table.Column<int>(type: "integer", nullable: true),
                    low_s = table.Column<int>(type: "integer", nullable: true),
                    rest_s = table.Column<int>(type: "integer", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stress", x => x.calendar_date);
                });

            migrationBuilder.CreateTable(
                name: "sync_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "text", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: true),
                    range_from = table.Column<DateOnly>(type: "date", nullable: true),
                    range_to = table.Column<DateOnly>(type: "date", nullable: true),
                    force = table.Column<bool>(type: "boolean", nullable: false),
                    items_done = table.Column<int>(type: "integer", nullable: false),
                    items_total = table.Column<int>(type: "integer", nullable: false),
                    errors = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    message = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "weight",
                columns: table => new
                {
                    timestamp_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calendar_date = table.Column<DateOnly>(type: "date", nullable: false),
                    weight_kg = table.Column<double>(type: "double precision", nullable: true),
                    bmi = table.Column<double>(type: "double precision", nullable: true),
                    body_fat_pct = table.Column<double>(type: "double precision", nullable: true),
                    body_water_pct = table.Column<double>(type: "double precision", nullable: true),
                    muscle_mass = table.Column<double>(type: "double precision", nullable: true),
                    bone_mass = table.Column<double>(type: "double precision", nullable: true),
                    raw = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weight", x => x.timestamp_utc);
                });

            migrationBuilder.CreateIndex(
                name: "ix_activities_start_time_local",
                table: "activities",
                column: "start_time_local");

            migrationBuilder.CreateIndex(
                name: "ix_snapshots_dataset_fetched_at",
                table: "snapshots",
                columns: new[] { "dataset", "fetched_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activities");

            migrationBuilder.DropTable(
                name: "activity_files");

            migrationBuilder.DropTable(
                name: "activity_laps");

            migrationBuilder.DropTable(
                name: "activity_records");

            migrationBuilder.DropTable(
                name: "auth_token");

            migrationBuilder.DropTable(
                name: "body_battery");

            migrationBuilder.DropTable(
                name: "daily_summary");

            migrationBuilder.DropTable(
                name: "health_chunks");

            migrationBuilder.DropTable(
                name: "heart_rate_daily");

            migrationBuilder.DropTable(
                name: "hrv");

            migrationBuilder.DropTable(
                name: "hydration");

            migrationBuilder.DropTable(
                name: "raw_daily");

            migrationBuilder.DropTable(
                name: "sleep");

            migrationBuilder.DropTable(
                name: "snapshots");

            migrationBuilder.DropTable(
                name: "steps");

            migrationBuilder.DropTable(
                name: "stress");

            migrationBuilder.DropTable(
                name: "sync_jobs");

            migrationBuilder.DropTable(
                name: "weight");
        }
    }
}
