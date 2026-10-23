#!/usr/bin/env python3
"""Garmin Connect Personal API probe (throwaway tool, stdlib only).

Verifies endpoint behaviour and the plan's Field Mapping on a real account.
Token is requested at most once and cached in ~/.config/garmin-mcp/probe-token.
Real responses go to samples/ (gitignored). Never prints the key or token.
"""
import base64, datetime as dt, json, os, sys, time, urllib.error, urllib.request

BASE = "https://personal.connectapi.garmin.com"
CFG = os.path.expanduser("~/.config/garmin-mcp")
KEY_FILE = os.path.join(CFG, "api-key")
TOKEN_FILE = os.path.join(CFG, "probe-token")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SAMPLES = os.path.join(ROOT, "samples")
RATE_HEADERS = ("retry-after",)
seen_rate = {}


def jwt_exp(tok):
    try:
        p = tok.split(".")[1]
        p += "=" * (-len(p) % 4)
        return int(json.loads(base64.urlsafe_b64decode(p))["exp"])
    except Exception:
        return 0


def get_token():
    try:
        with open(TOKEN_FILE) as f:
            d = json.load(f)
        if d.get("exp", 0) > time.time() + 60:
            return d["token"]
    except Exception:
        pass
    with open(KEY_FILE) as f:
        key = f.read().strip()
    req = urllib.request.Request(
        BASE + "/personalapitoken-service/personal-api/personal-api-key/token",
        method="POST", headers={"Authorization": "ApiKey " + key})
    try:
        with urllib.request.urlopen(req, timeout=30) as r:
            tok = r.read().decode().strip()
    except urllib.error.HTTPError as e:
        if e.code == 401:
            print("TOKEN 401 — zatrzymaj się i zapytaj właściciela")
        else:
            print("TOKEN HTTP", e.code)
        sys.exit(2)
    fd = os.open(TOKEN_FILE, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w") as f:
        json.dump({"token": tok, "exp": jwt_exp(tok)}, f)
    os.chmod(TOKEN_FILE, 0o600)
    return tok


TOKEN = None


def http(path):
    """GET; returns (status, bytes, headers). Never raises on HTTP errors."""
    time.sleep(0.5)
    req = urllib.request.Request(BASE + path, headers={"Authorization": "Bearer " + TOKEN})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            status, body, hdrs = r.status, r.read(), r.headers
    except urllib.error.HTTPError as e:
        status, body, hdrs = e.code, e.read(), e.headers
    except Exception as e:
        return -1, str(e).encode(), {}
    for k, v in hdrs.items():
        kl = k.lower()
        if kl in RATE_HEADERS or kl.startswith("x-ratelimit") or kl.startswith("ratelimit"):
            seen_rate[kl] = v
    return status, body, hdrs


def shape(v, depth=0):
    if isinstance(v, list):
        return f"list[{len(v)}]" + (" of " + shape(v[0], depth + 1) if v and depth < 1 else "")
    if isinstance(v, dict):
        ks = list(v)
        return "dict{" + ",".join(ks[:12]) + (",..." if len(ks) > 12 else "") + "}"
    return type(v).__name__


def fetch_json(name, label, path):
    st, body, _ = http(path)
    data = None
    if body:
        try:
            data = json.loads(body)
        except Exception:
            pass
    os.makedirs(SAMPLES, exist_ok=True)
    out = os.path.join(SAMPLES, f"{name}-{label}.json")
    with open(out, "wb") as f:
        f.write(body or b"")
    print(f"  {name:20s} {label:7s} HTTP {st} bytes={len(body)} shape={shape(data) if data is not None else 'non-json/empty'}")
    return st, data


# ---------- path checking ----------
def resolve(obj, path):
    cur = obj
    for part in path.split("."):
        if isinstance(cur, dict) and part in cur:
            cur = cur[part]
        else:
            return False, None
    return True, cur


def check_paths(label, records, paths):
    if not records:
        print(f"  [{label}] NO RECORDS in sample - cannot verify")
        return
    for p in paths:
        alts = p.split(" ?? ")
        found = types = None
        n = 0
        tset = set()
        for rec in records:
            for a in alts:
                ok, v = resolve(rec, a)
                if ok:
                    n += 1
                    tset.add(type(v).__name__)
                    break
        if n:
            print(f"  [{label}] OK {p} present {n}/{len(records)} types={sorted(tset)}")
        else:
            print(f"  [{label}] MISSING {p}")


def as_list(data, key=None):
    if key and isinstance(data, dict):
        data = data.get(key)
    return [x for x in data if isinstance(x, dict)] if isinstance(data, list) else []


def main():
    global TOKEN
    TOKEN = get_token()
    print("token ok (cached or fresh); exp in %ds" % (jwt_exp(TOKEN) - time.time()))
    today = dt.date.today()
    r7 = ((today - dt.timedelta(days=7)).isoformat(), today.isoformat())
    r22 = ("2022-06-01", "2022-06-28")
    d22, d7 = "2022-06-15", (today - dt.timedelta(days=1)).isoformat()

    ranged = {
        "daily_summary": "/usersummary-service/v2/dailysummaries?startDate={s}&endDate={e}",
        "sleep": "/sleep-service/sleep/dailySleepSummariesByDate?startDate={s}&endDate={e}",
        "hrv": "/hrv-service/hrv/daily/{s}/{e}",
        "body_battery": "/wellness-service/wellness/bodyBattery/reports/daily?startDate={s}&endDate={e}",
        "stress": "/usersummary-service/stats/stress/daily/{s}/{e}",
        "steps": "/usersummary-service/stats/steps/daily/{s}/{e}",
        "hydration": "/usersummary-service/stats/hydration/daily/{s}/{e}",
        "weight": "/weight-service/weight/all/{s}/{e}",
        "wellness_activities": "/wellnessactivity-service/activity/summary/range/{s}/{e}",
    }
    daily = {
        "heart_rate_daily": "/wellness-service/wellness/dailyHeartRate?date={d}",
        "spo2": "/wellness-service/wellness/daily/spo2acclimation/{d}",
        "nutrition": "/nutrition-service/food/logs/{d}",
        "menstrual_summary": "/periodichealth-service/menstrualcycle/summary/{d}",
        "menstrual_dayview": "/periodichealth-service/menstrualcycle/dayview/{d}",
    }
    S = {}
    print("== ranged datasets ==")
    for name, t in ranged.items():
        for label, (s, e) in (("2022", r22), ("recent", r7)):
            S[(name, label)] = fetch_json(name, label, t.format(s=s, e=e))
    print("== daily datasets ==")
    for name, t in daily.items():
        for label, d in (("2022", d22), ("recent", d7)):
            S[(name, label)] = fetch_json(name, label, t.format(d=d))
    print("== snapshots ==")
    snaps = {
        "hr_zones": "/biometric-service/heartRateZones/v2",
        "user_profile": "/userprofile-service/userprofile/settings",
        "fitness_age": "/fitnessage-service/fitnessage/v2/{today}",
        "weight_goals": "/goal-service/goal/user/effective/weightgoal/2021-09-23/{today}",
    }
    for name, t in snaps.items():
        t = t.format(today=today.isoformat())
        S[(name, "recent")] = fetch_json(name, "recent", t)
        if name == "fitness_age":
            S[(name, "2022")] = fetch_json(name, "2022", "/fitnessage-service/fitnessage/v2/" + d22)

    # ---------- activities ----------
    print("== activities ==")
    lst = "/activitylist-service/activities/search/activities"
    st0, a0 = fetch_json("activities", "p0", f"{lst}?start=0&limit=99")
    st1, a1 = fetch_json("activities", "p1", f"{lst}?start=99&limit=99")
    L0 = as_list(a0) or as_list(a0, "activities")
    L1 = as_list(a1) or as_list(a1, "activities")
    ids0 = {x.get("activityId") for x in L0}
    ids1 = {x.get("activityId") for x in L1}
    print(f"  page0 n={len(L0)} page1 n={len(L1)} overlap={len(ids0 & ids1)} pagination_works={bool(L1) and not (ids0 & ids1)}")
    # full count
    all_ids, pages, repeated = [], 0, False
    start = 0
    allacts = []
    while pages < 60:
        st, d = fetch_json("activities_tmp", "page", f"{lst}?start={start}&limit=99")
        L = as_list(d)
        if st != 200 or not L:
            break
        new = [x for x in L if x.get("activityId") not in set(all_ids)]
        if not new:
            repeated = True
            break
        for x in new:
            all_ids.append(x.get("activityId"))
            allacts.append(x)
        pages += 1
        start += 99
        if len(L) < 99:
            break
    try:
        os.remove(os.path.join(SAMPLES, "activities_tmp-page.json"))
    except OSError:
        pass
    print(f"  total_activities={len(all_ids)} pages={pages} hit_repeat_guard={repeated} hit_page_cap={pages >= 60}")
    with open(os.path.join(SAMPLES, "activities-all.json"), "w") as f:
        json.dump(allacts, f)
    # date filter
    st, df = fetch_json("activities", "datefilter", f"{lst}?start=0&limit=99&startDate=2022-06-01&endDate=2022-06-28")
    DF = as_list(df)
    sts = sorted(str(x.get("startTimeLocal", ""))[:10] for x in DF)
    inrange = all("2022-06-01" <= s <= "2022-06-28" for s in sts) if sts else None
    print(f"  datefilter n={len(DF)} all_in_range={inrange} total_vs_filtered={len(all_ids)}>{len(DF)}")
    # oldest / newest
    chosen = []
    if allacts:
        key = lambda x: str(x.get("startTimeGMT") or x.get("startTimeLocal") or "")
        so = sorted(allacts, key=key)
        chosen = [("oldest", so[0]), ("newest", so[-1])]
    for lab, a in chosen:
        aid = a["activityId"]
        print(f"  -- {lab} activity (date {str(a.get('startTimeLocal'))[:10]})")
        fetch_json("activity_detail", lab, f"/activity-service/activity/{aid}")
        fetch_json("activity_workouts", lab, f"/activity-service/activity/{aid}/workouts")
        st, body, hd = http(f"/download-service/files/activity/{aid}")
        magic = body[:4]
        inner = ""
        if body[:2] == b"PK":
            import io, zipfile
            try:
                inner = " zip_members=" + ",".join(os.path.splitext(n)[1] or "?" for n in zipfile.ZipFile(io.BytesIO(body)).namelist())
            except Exception as e:
                inner = " zip_err"
        elif body[:5] == b"<?xml":
            inner = " xml"
        elif body[8:12] == b".FIT":
            inner = " raw_fit"
        ext = "zip" if body[:2] == b"PK" else "bin"
        with open(os.path.join(SAMPLES, f"activity_file-{lab}.{ext}"), "wb") as f:
            f.write(body or b"")
        print(f"  activity_file {lab} HTTP {st} bytes={len(body)} magic={magic!r} ctype={hd.get('content-type') if hd else None}{inner}")

    # ---------- Field Mapping verification ----------
    print("== field mapping ==")
    act_paths = ["activityId", "activityName", "activityType.typeKey", "eventType.typeKey", "startTimeLocal", "startTimeGMT",
                 "distance", "duration", "movingDuration", "elapsedDuration", "averageHR", "maxHR", "averageSpeed", "maxSpeed",
                 "averageRunningCadenceInStepsPerMinute", "averageBikingCadenceInRevPerMinute", "averageSwimCadenceInStrokesPerMinute",
                 "elevationGain", "calories", "avgPower", "activityTrainingLoad", "intensityFactor", "aerobicTrainingEffect",
                 "anaerobicTrainingEffect"] + [f"hrTimeInZone_{i}" for i in range(1, 6)]
    check_paths("activities", allacts, act_paths)
    print("  (cadence alternatives are independent per sport; check each above)")

    def recs(name, key=None):
        out = []
        for label in ("2022", "recent"):
            st, d = S.get((name, label), (0, None))
            out += as_list(d, key)
        return out

    check_paths("daily_summary", recs("daily_summary", "data"),
                ["calendarDate", "movement.steps", "calories.burnedTotal", "calories.burnedActive", "heartRate.restingValue",
                 "heartRate.minValue", "heartRate.maxValue", "stress.avgLevel", "stress.maxLevel", "bodyBattery.minValue",
                 "bodyBattery.maxValue", "intensityMinutes.moderate", "intensityMinutes.vigorous"])
    # tolerate bare-list responses for daily_summary
    if not recs("daily_summary", "data"):
        print("  [daily_summary] 'data[]' wrapper missing; top-level shape:",
              [shape(S[("daily_summary", l)][1]) for l in ("2022", "recent")])
    check_paths("sleep", recs("sleep"),
                ["calendarDate", "sleepTimeSeconds", "deepSleepSeconds", "lightSleepSeconds", "remSleepSeconds", "awakeSleepSeconds",
                 "sleepScores.overall.value", "avgHeartRate", "averageRespirationValue", "avgSleepStress",
                 "sleepStartTimestampLocal", "sleepEndTimestampLocal"])
    check_paths("hrv", recs("hrv", "hrvSummaries"),
                ["calendarDate", "lastNightAvg", "weeklyAvg", "lastNight5MinHigh", "status", "baseline.balancedLow", "baseline.balancedUpper"])
    bb = recs("body_battery")
    check_paths("body_battery", bb, ["date", "charged", "drained", "bodyBatteryValuesArray", "bodyBatteryValueDescriptorDTOList"])
    for r in bb[:1]:
        print("  [body_battery] descriptor keys:", sorted({k for d in r.get("bodyBatteryValueDescriptorDTOList", []) if isinstance(d, dict) for k in d}),
              "descriptor names:", [d.get("bodyBatteryValueDescriptorKey") for d in r.get("bodyBatteryValueDescriptorDTOList", []) if isinstance(d, dict)])
    check_paths("stress", recs("stress"),
                ["calendarDate", "values.overallStressLevel", "values.highStressDuration", "values.mediumStressDuration",
                 "values.lowStressDuration", "values.restStressDuration"])
    check_paths("steps", recs("steps"), ["calendarDate", "totalSteps", "totalDistance", "stepGoal"])
    check_paths("hydration", recs("hydration"), ["calendarDate", "valueInML", "goalInML", "sweatLossInML"])
    check_paths("weight", recs("weight"), ["timestampGMT", "calendarDate", "weight", "bmi", "bodyFat", "bodyWater", "muscleMass", "boneMass"])
    for label in ("2022", "recent"):
        st, d = S[("weight", label)]
        if isinstance(d, dict):
            print(f"  [weight/{label}] top-level is dict (not list); keys:", list(d)[:10])
    hr = [d for l in ("2022", "recent") for st, d in [S[("heart_rate_daily", l)]] if isinstance(d, dict)]
    check_paths("heart_rate_daily", hr, ["calendarDate", "restingHeartRate", "minHeartRate", "maxHeartRate", "lastSevenDaysAvgRestingHeartRate"])

    print("== rate-limit headers seen ==")
    print(" ", seen_rate or "none")


if __name__ == "__main__":
    main()
