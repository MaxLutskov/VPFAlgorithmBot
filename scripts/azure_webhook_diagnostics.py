"""Print only error classes and project stack-frame names from App Service logs."""

import collections
import re
import subprocess
import tempfile
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
                    for method in re.findall(r"\bat (VPFAlgorithmBot\.[A-Za-z0-9_.+<>]+)\(", line):
                        frames[method] += 1
    print("Exception classes:", errors.most_common(20))
    print("Project stack frames:", frames.most_common(20))
