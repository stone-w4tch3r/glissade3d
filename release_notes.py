"""Extract a dated Keep a Changelog section without deriving notes from Git."""

import argparse
from datetime import date
from pathlib import Path
import re


def release_notes(text, version):
    version = version[1:] if version.startswith("v") else version
    if not re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version):
        raise ValueError("Expected a four-part release version, optionally prefixed with v.")
    headings = list(re.finditer(r"^## .*$", text, re.MULTILINE))
    matches = []
    for index, heading in enumerate(headings):
        match = re.fullmatch(r"## \[([^\]]+)\](?: - (\S+))?[ \t]*", heading.group().rstrip("\r"))
        if match and match[1] == version:
            end = headings[index + 1].start() if index + 1 < len(headings) else len(text)
            matches.append((match[2], text[heading.end():end].strip()))
    if len(matches) != 1:
        raise ValueError(f"Expected exactly one changelog section for {version}; found {len(matches)}.")
    released, body = matches[0]
    try:
        if date.fromisoformat(released).isoformat() != released:
            raise ValueError()
    except (TypeError, ValueError):
        raise ValueError(f"Section {version} needs a valid YYYY-MM-DD release date.") from None
    if not re.search(r"^- \S", body, re.MULTILINE):
        raise ValueError(f"Section {version} needs at least one changelog bullet.")
    return body + "\n"


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("version")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        notes = release_notes(Path(__file__).with_name("CHANGELOG.md").read_text(encoding="utf-8"), args.version)
    except ValueError as error:
        parser.error(str(error))
    if args.output:
        with args.output.open("w", encoding="utf-8", newline="\n") as output:
            output.write(notes)
    else:
        print(notes, end="")
