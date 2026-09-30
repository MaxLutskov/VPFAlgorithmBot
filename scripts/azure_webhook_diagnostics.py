"""Print only error classes and project stack-frame names from App Service logs."""

import collections
import re
import subprocess
import tempfile
import json
import urllib.request
import zipfile
from pathlib import Path


with tempfile.TemporaryDirectory() as directory:
    archive = Path(directory) / "logs.zip"
    command = subprocess.run(
        ["az", "webapp", "log", "download", "-g", "asp", "-n", "VPFAlgorithmBot",
         "--log-file", str(archive), "--only-show-errors"],
        text=True, capture_output=True, timeout=90,
    )
    if command.returncode or not archive.exists():
        print("Log download unavailable; CLI exit code:", command.returncode)
        raise SystemExit(0)

    errors = collections.Counter()
    frames = collections.Counter()
    http_statuses = collections.Counter()
    with zipfile.ZipFile(archive) as logs:
        files = [name for name in logs.namelist() if name.lower().endswith((".log", ".txt"))]
        print("Log files inspected:", len(files))
        for name in files:
            with logs.open(name) as source:
                for raw in source:
                    line = raw.decode("utf-8", errors="replace")[:4096]
                    if "Exception" in line or "Error" in line:
                        for kind in re.findall(r"\b(?:System|Microsoft|VPFAlgorithmBot)\.[A-Za-z0-9_.]+(?:Exception|Error)\b", line):
                            errors[kind] += 1
                    for status in re.findall(r"Response status code does not indicate success: (\d{3})", line):
                        http_statuses[status] += 1
                    for method in re.findall(r"\bat (VPFAlgorithmBot\.[A-Za-z0-9_.+<>]+)\(", line):
                        frames[method] += 1
    print("Exception classes:", errors.most_common(20))
    print("Outbound HTTP status codes:", http_statuses.most_common())
    print("Project stack frames:", frames.most_common(20))

settings_result = subprocess.run(
    ["az", "webapp", "config", "appsettings", "list", "-g", "asp", "-n", "VPFAlgorithmBot", "-o", "json", "--only-show-errors"],
    text=True, capture_output=True, timeout=60,
)
if settings_result.returncode:
    print("Webhook status unavailable: Azure settings query failed")
else:
    settings = {item["name"]: item.get("value") for item in json.loads(settings_result.stdout)}
    token = settings.get("Telegram__BotToken")
    secret = settings.get("Telegram__WebhookSecret")
    if not token:
        print("Webhook status unavailable: bot token is missing")
    else:
        try:
            with urllib.request.urlopen(f"https://api.telegram.org/bot{token}/getWebhookInfo", timeout=20) as response:
                info = json.load(response)["result"]
            error = info.get("last_error_message") or ""
            print("Webhook status:", {
                "url_matches_secret_path": bool(secret and info.get("url", "").endswith("/api/telegram/" + secret)),
                "pending_updates": info.get("pending_update_count"),
                "last_error_at_utc": info.get("last_error_date"),
                "last_error_http_status": re.findall(r"\b[45]\d\d\b", error),
            })
        except Exception as exc:
            print("Webhook status unavailable:", type(exc).__name__)
