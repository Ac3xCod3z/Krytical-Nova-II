"""
Kill test of the save service (online phase 4, milestone 3): trades between test accounts while the save service is
killed (taskkill /F) at random moments and started again, many times, and the game server itself "crashes" now and then
(drops the batch it was sending). Afterwards nothing may be duplicated or lost, and the ledger must agree with the saves.

TEST database only: it starts its own save service with --test (port 7802, krytical_nova_test) and refuses to run if
something already listens there. Its documents live under killtest/<run>/ and are deleted at the end.

    python kill_test.py <SaveService.dll or SaveService.exe> [trades, 400] [accounts, 6]

Reads the service key from Documents/KryticalNova-Secrets/save-service-key.txt; never prints it.
"""
import json
import os
import random
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.request
import uuid

URL = 'http://127.0.0.1:7802'
KEY = open(os.path.expanduser(r'~\Documents\KryticalNova-Secrets\save-service-key.txt')).read().strip()
SERVICE = sys.argv[1]
TRADES = int(sys.argv[2]) if len(sys.argv) > 2 else 400
ACCOUNTS = int(sys.argv[3]) if len(sys.argv) > 3 else 6
RUN = uuid.uuid4().hex[:8]
START_CREDS, START_ITEMS = 1000, 20
ITEMS = ['1203', '40', '9031']


def listening():
    with socket.socket() as s:
        s.settimeout(0.3)
        return s.connect_ex(('127.0.0.1', 7802)) == 0


class Service:
    """The test save service, started and killed by this script."""

    def __init__(self):
        self.process = None
        self.kills = 0
        self.lock = threading.Lock()

    def start(self):
        command = ['dotnet', SERVICE, '--test'] if SERVICE.endswith('.dll') else [SERVICE, '--test']
        self.process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        for _ in range(100):
            if listening():
                return
            time.sleep(0.1)
        raise RuntimeError('the test save service did not start')

    def kill(self):
        with self.lock:
            subprocess.run(['taskkill', '/F', '/PID', str(self.process.pid)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            self.process.wait()
            self.kills += 1
            time.sleep(random.uniform(0.0, 0.8))
            self.start()

    def stop(self):
        subprocess.run(['taskkill', '/F', '/PID', str(self.process.pid)], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def call(method, path, body=None, timeout=3):
    request = urllib.request.Request(URL + path, data=None if body is None else json.dumps(body).encode(), method=method)
    request.add_header('X-KN-Key', KEY)
    request.add_header('Content-Type', 'application/json')
    try:
        with urllib.request.urlopen(request, timeout=timeout) as answer:
            return answer.status, answer.read().decode()
    except urllib.error.HTTPError as refused:
        return refused.code, refused.read().decode()


def wallet_key(i): return f'killtest/{RUN}/w{i}'
def pack_key(i): return f'killtest/{RUN}/p{i}'
def account(i): return f'killtest-{RUN}-{i}'


def read_all():
    keys = [wallet_key(i) for i in range(ACCOUNTS)] + [pack_key(i) for i in range(ACCOUNTS)]
    status, text = call('POST', '/read', {'keys': keys}, timeout=10)
    assert status == 200, text
    docs = {d['key']: d for d in json.loads(text)}
    versions = {k: docs[k]['version'] for k in docs}
    wallets = [json.loads(docs[wallet_key(i)]['body'])['creds'] for i in range(ACCOUNTS)]
    packs = [json.loads(docs[pack_key(i)]['body']) for i in range(ACCOUNTS)]
    return versions, wallets, packs


def ledger_line(i, other, creds, items):
    return {'id': uuid.uuid4().hex, 'utc': int(time.time() * 10_000_000) + 621355968000000000, 'kind': 'trade',
            'account': account(i), 'character': 'c', 'name': f'Tester{i}', 'otherAccount': account(other),
            'creds': creds, 'items': [{'item': int(k), 'count': v, 'name': ''} for k, v in items.items()], 'note': 'kill test'}


def main():
    if listening():
        sys.exit('Something already listens on 7802: stop the test save service first (this test starts its own).')
    service = Service()
    service.start()
    # The accounts' starting state, one batch.
    start = {'id': uuid.uuid4().hex, 'deletes': [], 'ledger': [], 'writes':
             [{'key': wallet_key(i), 'body': json.dumps({'creds': START_CREDS}), 'expected': 0} for i in range(ACCOUNTS)] +
             [{'key': pack_key(i), 'body': json.dumps({k: START_ITEMS for k in ITEMS}), 'expected': 0} for i in range(ACCOUNTS)]}
    status, text = call('POST', '/batch', start)
    assert status == 200, text
    versions, wallets, packs = read_all()

    stop_killing = threading.Event()

    def killer():
        while not stop_killing.wait(random.uniform(0.05, 0.6)):
            service.kill()

    threading.Thread(target=killer, daemon=True).start()

    sent = resent = crashes = 0
    for _ in range(TRADES):
        a, b = random.sample(range(ACCOUNTS), 2)
        creds = random.randint(1, 60) if wallets[a] >= 60 else 0
        item = random.choice(ITEMS)
        give = 1 if packs[b].get(item, 0) > 0 else 0
        wa, wb = wallets[a] - creds, wallets[b] + creds
        pa, pb = dict(packs[a]), dict(packs[b])
        pa[item] = pa.get(item, 0) + give
        pb[item] = pb.get(item, 0) - give
        batch = {'id': uuid.uuid4().hex, 'deletes': [],
                 'writes': [{'key': wallet_key(a), 'body': json.dumps({'creds': wa}), 'expected': versions[wallet_key(a)]},
                            {'key': wallet_key(b), 'body': json.dumps({'creds': wb}), 'expected': versions[wallet_key(b)]},
                            {'key': pack_key(a), 'body': json.dumps(pa), 'expected': versions[pack_key(a)]},
                            {'key': pack_key(b), 'body': json.dumps(pb), 'expected': versions[pack_key(b)]}],
                 'ledger': [ledger_line(a, b, -creds, {item: give}), ledger_line(b, a, creds, {item: -give})]}
        sent += 1
        while True:
            try:
                status, text = call('POST', '/batch', batch)
            except (urllib.error.URLError, ConnectionError, TimeoutError, socket.timeout):
                # No answer. Now and then the game server "crashes" here: the batch is gone with it, and the next
                # start reads what the database holds. Otherwise it sends the same batch again, as the game does.
                if random.random() < 0.15:
                    crashes += 1
                    while True:
                        try:
                            versions, wallets, packs = read_all()
                            break
                        except Exception:
                            time.sleep(0.2)
                    break
                resent += 1
                time.sleep(0.2)
                continue
            if status != 200:
                stop_killing.set()
                sys.exit(f'FAILED: a batch was refused ({status}): {text}')
            for written in json.loads(text)['versions']:
                versions[written['key']] = written['version']
            wallets[a], wallets[b], packs[a], packs[b] = wa, wb, pa, pb
            break

    stop_killing.set()
    time.sleep(3)
    while not listening():
        time.sleep(0.2)

    # What the database holds, against itself and its ledger.
    versions, wallets, packs = read_all()
    problems = []
    if sum(wallets) != START_CREDS * ACCOUNTS:
        problems.append(f'Creds made or lost: {sum(wallets)} in all, {START_CREDS * ACCOUNTS} at the start')
    for item in ITEMS:
        total = sum(p.get(item, 0) for p in packs)
        if total != START_ITEMS * ACCOUNTS:
            problems.append(f'item {item} made or lost: {total} in all, {START_ITEMS * ACCOUNTS} at the start')
    lines_total = 0
    for i in range(ACCOUNTS):
        status, text = call('GET', f'/ledger?account={account(i)}&since=0&limit=50000', timeout=10)
        lines = json.loads(text)
        lines_total += len(lines)
        ids = [line['id'] for line in lines]
        if len(ids) != len(set(ids)):
            problems.append(f'account {i}: a ledger line is there twice')
        creds = START_CREDS + sum(line['creds'] for line in lines)
        if creds != wallets[i]:
            problems.append(f'account {i}: wallet {wallets[i]}, ledger says {creds}')
        for item in ITEMS:
            count = START_ITEMS + sum(x['count'] for line in lines for x in line['items'] if str(x['item']) == item)
            if count != packs[i].get(item, 0):
                problems.append(f'account {i}: item {item} {packs[i].get(item, 0)}, ledger says {count}')
    applied = lines_total // 2

    # Clean up the test documents (the ledger lines are left in the TEST database; it trims them itself).
    call('POST', '/batch', {'id': uuid.uuid4().hex, 'writes': [], 'ledger': [],
                            'deletes': [wallet_key(i) for i in range(ACCOUNTS)] + [pack_key(i) for i in range(ACCOUNTS)]})
    service.stop()

    print(f'Run {RUN}: {sent} trades sent, {applied} applied, {resent} sent again after no answer, '
          f'{crashes} dropped by a game-server "crash", {service.kills} kills of the save service.')
    if problems:
        print('FAILED:')
        for problem in problems:
            print('  ' + problem)
        sys.exit(1)
    print('PASSED: no Creds or items made or lost; every wallet and Pack equals its start plus its ledger lines; no ledger line twice.')


if __name__ == '__main__':
    main()
