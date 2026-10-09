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
    # Inspect adjacent pages and deliberately overlapping ranges. All requests
    # are read-only. Compare IDs in memory; never print their actual values.
    ranges = [(1, 10), (11, 20), (20, 24), (21, 24), (24, 24)]
    pages = []
    for first, last in ranges:
        root = request(opener, "get_action_history.json",
                       {"product_id": product, "locale": "nl_NL",
                        "startindex": str(first), "stopindex": str(last)})
        data = root.get("data", {})
        actions = data.get("actions", [])
        ids = [str(a["atn_id"]) for a in actions if a.get("atn_id")]
        pages.append({
            "range": f"{first}-{last}",
            "reported_start": data.get("startindex"),
            "reported_stop": data.get("stopindex"),
            "reported_max": data.get("maxindex"),
            "records": len(actions),
            "missing_id_count": len(actions) - len(ids),
            "duplicate_ids_within_page": len(ids) - len(set(ids)),
            "_ids": set(ids),
        })

    # The reference ranges overlap at index 20. Count that expected overlap
    # separately, then check whether range 21-24 or singleton 24 reveals
    # an ID not already present in the reference union.
    reference = pages[:3]
    reference_ids = [item for page in reference for item in page["_ids"]]
    reference_union = set(reference_ids)
    reference_duplicates = len(reference_ids) - len(reference_union)
    suspicious = pages[3]["_ids"]
    terminal = pages[4]["_ids"]
    # Reconstruct the current 10-item reader behavior for the observed
    # 24-index dataset: page 0, page 1, then terminal page with the
    # one-index overlap recovery (20-24). No raw identifiers leave memory.
    normal_union = pages[0]["_ids"] | pages[1]["_ids"] | pages[3]["_ids"]
    recovered_union = normal_union | pages[2]["_ids"]
    reader_comparison = {
        "expected_reference_unique_ids": len(reference_union),
        "standard_reader_unique_ids": len(normal_union),
        "with_terminal_overlap_unique_ids": len(recovered_union),
        "overlap_recovers_reference": recovered_union == reference_union,
        "additional_ids_recovered": len(recovered_union - normal_union),
        "no_ids_missing_from_reference": not (reference_union - recovered_union),
        "same_maxindex_in_all_ranges": len({
            str(page["reported_max"]) for page in pages
        }) == 1,
    }
    comparisons = []
    for index, page in enumerate(pages):
        for other in pages[index + 1:]:
            comparisons.append({
                "ranges": [page["range"], other["range"]],
                "shared_action_ids": len(page["_ids"] & other["_ids"])
            })

    print(json.dumps({
        "success": True,
        "pages": [{k: v for k, v in page.items() if k != "_ids"} for page in pages],
        "reference_ranges": {
            "ranges": [page["range"] for page in reference],
            "unique_action_ids": len(reference_union),
            "overlap_occurrences": reference_duplicates,
            "all_have_ids": all(page["missing_id_count"] == 0 for page in reference)
        },
        "boundary_check": {
            "21-24_unique_ids": len(suspicious),
            "21-24_ids_in_reference": len(suspicious & reference_union),
            "21-24_ids_missing_from_reference": len(suspicious - reference_union),
            "24-24_ids_in_reference": len(terminal & reference_union),
            "24-24_ids_already_in_21-24": len(terminal & suspicious)
        },
        "overlap_checks": comparisons,
        "reader_comparison": reader_comparison,
        "note": "Shared IDs in intentionally overlapping ranges are expected; "
                "shared IDs in disjoint ranges indicate a potential paging issue."
    }, indent=2, ensure_ascii=False))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        sys.exit("2Park read-only probe failed. No response details printed; check credentials/network privately.")
