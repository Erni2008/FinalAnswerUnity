#!/usr/bin/env python3
"""Verify copied Java assets; no dependency on the Java checkout for normal use."""
import argparse
import hashlib
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--compare-java', type=Path)
args = parser.parse_args()
manifest = json.loads((root / 'ImportedAssets.json').read_text())
for entry in manifest['files']:
    target = root / entry['unity']
    assert target.is_file(), f'Missing {target}'
    assert hashlib.sha256(target.read_bytes()).hexdigest() == entry['sha256'], str(target)
    if args.compare_java:
        source = args.compare_java / entry['source']
        assert hashlib.sha256(source.read_bytes()).hexdigest() == entry['sha256'], str(source)
print(f"IMPORTS_OK: {len(manifest['files'])} copied assets match source SHA-256")
