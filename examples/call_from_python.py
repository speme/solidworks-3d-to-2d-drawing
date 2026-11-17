"""Invoke the console interface without a shell; preserves spaces and Chinese paths."""
import json
from pathlib import Path
import subprocess
import sys

cli = Path(sys.argv[1]).resolve()
request = Path(sys.argv[2]).resolve()
process = subprocess.run(
    [str(cli), "generate", "--request", str(request)],
    capture_output=True, encoding="utf-8", check=False,
)
sys.stderr.write(process.stderr)
result = json.loads(process.stdout)
if process.returncode != 0:
    raise RuntimeError(f"{result['ErrorCode']}: {result['Error'] or result['Jobs']}")
for job in result["Jobs"]:
    print(job["Drawing"])
# Exit 0 confirms generation; generated_review_required still requires engineering review.
