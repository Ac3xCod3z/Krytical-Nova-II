"""Reads `hcloud server-type describe <type> -o json` on stdin and prints "<hourly> <monthly>" (gross, in the account's currency)
for the location given as argv[1]. Exits 1 when the type has no price there."""
import json
import sys

data = json.load(sys.stdin)
for price in data.get("prices", []):
    if price.get("location") == sys.argv[1]:
        print(price["price_hourly"]["gross"], price["price_monthly"]["gross"])
        raise SystemExit(0)
raise SystemExit(1)
