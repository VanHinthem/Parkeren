#!/usr/bin/env python3
"""Read-only, privacy-preserving 2Park history probe (issue #153).

Environment: PARKEREN_TWOPARK_EMAIL, PARKEREN_TWOPARK_PASSWORD,
PARKEREN_TWOPARK_PRODUCT_ID. Never stores or prints raw provider responses.
"""
import json
import os
import sys
from collections import Counter
from http.cookiejar import CookieJar
from urllib.parse import urlencode
from urllib.request import HTTPCookieProcessor, Request, build_opener

BASE = "https://mijn.2park.nl/gsmpark-app-www/json/"
ENDPOINTS = {"check_credentials.json", "get_action_history.json"}


def request(opener, endpoint, fields):
    if endpoint not in ENDPOINTS:
        raise ValueError("Only authentication and history reads are allowed")
    req = Request(BASE + endpoint, data=urlencode(fields).encode("utf-8"), method="POST")
    with opener.open(req, timeout=20) as response:
        body = response.read(1_000_001)
    if len(body) > 1_000_000:
        raise ValueError("Response exceeds safety limit")
    root = json.loads(body)
    if root.get("status", {}).get("code", {}).get("major") != "OK":
        raise RuntimeError("Provider returned a non-OK status (details intentionally suppressed)")
    return root


def main():
    email = os.environ.get("PARKEREN_TWOPARK_EMAIL")
    password = os.environ.get("PARKEREN_TWOPARK_PASSWORD")
    product = os.environ.get("PARKEREN_TWOPARK_PRODUCT_ID")
    if not all((email, password, product)):
        sys.exit("Set PARKEREN_TWOPARK_EMAIL, PARKEREN_TWOPARK_PASSWORD and PARKEREN_TWOPARK_PRODUCT_ID.")
    opener = build_opener(HTTPCookieProcessor(CookieJar()))
    request(opener, "check_credentials.json",
            {"email": email, "password": password, "locale": "nl_NL"})
    # A single page only. Do not modify the application's existing pagination.
    root = request(opener, "get_action_history.json",
                   {"product_id": product, "locale": "nl_NL",
                    "startindex": "1", "stopindex": "10"})
    data = root.get("data", {})
    actions = data.get("actions", [])
    fields = {"MBR_IDENT", "TIMESTART", "TIMEEND", "LOCATION", "COST", "CURRENCY_DESC"}
    presence = Counter()
    statuses = Counter()
    for action in actions:
        statuses[str(action.get("atn_state", "missing")).upper()] += 1
        labels = {p.get("prr_label") for p in action.get("atn_parameters", [])}
        presence.update(fields & labels)
    print(json.dumps({
        "success": True,
        "records": len(actions),
        "startindex": data.get("startindex"),
        "stopindex": data.get("stopindex"),
        "maxindex": data.get("maxindex"),
        "statuses": dict(sorted(statuses.items())),
        "field_presence_counts": {key: presence[key] for key in sorted(fields)}
    }, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except Exception:
        sys.exit("2Park read-only probe failed. No response details printed; check credentials/network privately.")
