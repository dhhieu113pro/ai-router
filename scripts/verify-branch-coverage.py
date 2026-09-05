#!/usr/bin/env python3
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root_dir = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts/coverage")
reports = list(root_dir.rglob("coverage.cobertura.xml"))
if not reports:
    raise SystemExit("No coverage.cobertura.xml reports were produced.")

branches: dict[tuple[str, str, int], tuple[int, int]] = {}
pattern = re.compile(r"\((\d+)/(\d+)\)")

for report in reports:
    root = ET.parse(report).getroot()
    for package in root.findall("./packages/package"):
        package_name = package.attrib["name"]
        prefix = package_name + "/"
        for cls in package.findall(".//class"):
            filename = cls.attrib.get("filename")
            if not filename:
                continue
            normalized_filename = filename[len(prefix):] if filename.startswith(prefix) else filename
            for line in cls.findall("./lines/line"):
                if line.attrib.get("branch", "").lower() != "true":
                    continue
                match = pattern.search(line.attrib.get("condition-coverage", ""))
                if not match:
                    continue
                covered, total = map(int, match.groups())
                key = (package_name, normalized_filename, int(line.attrib["number"]))
                previous = branches.get(key, (0, 0))
                branches[key] = (max(previous[0], covered), max(previous[1], total))

total = sum(item[1] for item in branches.values())
covered = sum(item[0] for item in branches.values())
percentage = 100.0 * covered / total if total else 100.0
summary = f"Branch coverage: {percentage:.2f}% ({covered}/{total})"
print(summary)
with (root_dir / "summary.txt").open("a", encoding="utf-8") as handle:
    handle.write(summary + "\n")

if covered != total:
    for (package_name, filename, number), (line_covered, line_total) in sorted(branches.items()):
        if line_covered != line_total:
            print(f"PARTIAL {package_name}/{filename}:{number} {line_covered}/{line_total}")
    raise SystemExit(
        f"Branch coverage gate failed: expected 100.00%, got {percentage:.2f}% ({covered}/{total})."
    )
