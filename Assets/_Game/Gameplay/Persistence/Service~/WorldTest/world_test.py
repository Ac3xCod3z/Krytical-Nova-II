"""
World service test (online phase 5, milestone 2): presence of map servers and use-once travel tickets, through the save
service's /world/... calls. Two pretend map servers (Concord and Transit Row) report; a player gets a ticket from the
server they are on to the other map, and only that server may redeem it, once, within its 30 seconds.

TEST database only: it starts its own save service with --test (port 7802, krytical_nova_test) and refuses to run if
something already listens there. The world service keeps nothing in the database.

    python world_test.py <SaveService.dll or SaveService.exe>

Reads the service key from Documents/KryticalNova-Secrets/save-service-key.txt; never prints it.
"""
import json
import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request

URL = 'http://127.0.0.1:7802'
KEY = open(os.path.expanduser(r'~\Documents\KryticalNova-Secrets\save-service-key.txt')).read().strip()
SERVICE = sys.argv[1]
CONCORD, TRANSIT = 'test-pc:7790', 'test-pc:7792'
failures = []


def listening():
    with socket.socket() as s:
        s.settimeout(0.3)
        return s.connect_ex(('127.0.0.1', 7802)) == 0


def call(method, path, body=None, key=KEY):
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(URL + path, data=data, method=method,
                                     headers={'X-KN-Key': key, 'Content-Type': 'application/json'})
    try:
        with urllib.request.urlopen(request, timeout=5) as answer:
            text = answer.read().decode()
            return answer.status, json.loads(text) if text else None
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        return e.code, json.loads(text) if text else None


def check(what, ok):
    print(('  ok    ' if ok else '  FAIL  ') + what)
    if not ok:
        failures.append(what)


def report(server, world_map, name, port, players, address=None):
    body = {'server': server, 'map': world_map, 'name': name, 'port': port,
            'players': [{'account': a, 'character': c} for a, c in players]}
    if address is not None:
        body['address'] = address
    return call('POST', '/world/presence', body)


def main():
    if listening():
        print('Something already listens on 7802 (a test save service?). Stop it first; this test starts its own.')
        return 2
    command = ['dotnet', SERVICE, '--test'] if SERVICE.endswith('.dll') else [SERVICE, '--test']
    service = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(60):
            if listening():
                break
            time.sleep(0.5)
        # The port opens a moment before the service answers: wait for a real answer.
        for _ in range(40):
            try:
                if call('GET', '/health')[0] == 200:
                    break
            except OSError:
                pass
            time.sleep(0.5)
        print('World service test')

        status, _ = call('GET', '/world/servers', key='not-the-key')
        check('a call without the service key is refused', status == 401)

        # Concord names a local address (never handed out), Transit Row a public one (Linux plan item 2).
        status, answer = report(CONCORD, 'Zone01_StarterTown', 'Concord', 7790, [('acct-ash', 'Ash')], '127.0.0.1')
        check('Concord reports', status == 200)
        status, _ = report(TRANSIT, 'MeridianSprawl_Z1_TransitRow', 'Transit Row', 7792, [], '203.0.113.20')
        check('Transit Row reports', status == 200)
        status, _ = report('', 'x', 'x', 7790, [])
        check('a report without a server is refused', status == 400)

        status, servers = call('GET', '/world/servers')
        check('both map servers are listed', status == 200 and sorted(s['map'] for s in servers) ==
              ['MeridianSprawl_Z1_TransitRow', 'Zone01_StarterTown'])
        addresses = {s['map']: s.get('address') for s in servers}
        check('a public address is kept, a local one never', addresses.get('MeridianSprawl_Z1_TransitRow') == '203.0.113.20'
              and addresses.get('Zone01_StarterTown') is None)
        status, where = call('GET', '/world/where?account=acct-ash')
        check('Ash is on Concord', status == 200 and where['server'] == CONCORD and where['character'] == 'Ash')
        status, _ = call('GET', '/world/where?account=acct-nobody')
        check('an account on no server is not found', status == 404)

        _, answer = call('POST', '/world/ticket', {'account': 'acct-ash', 'character': 'Ash', 'from': TRANSIT,
                                                   'toMap': 'Zone01_StarterTown', 'arrival': 'portal'})
        check('only the server a player is on may ask for their ticket', not answer['granted'])
        _, answer = call('POST', '/world/ticket', {'account': 'acct-ash', 'from': CONCORD, 'toMap': 'EldersReach_Aldhavn'})
        check('no ticket to a map no server runs', not answer['granted'] and 'no server runs' in answer['refused'])

        _, ticket = call('POST', '/world/ticket', {'account': 'acct-ash', 'character': 'Ash', 'from': CONCORD,
                                                   'toMap': 'MeridianSprawl_Z1_TransitRow', 'arrival': 'portal-south'})
        check('a ticket to Transit Row is granted with its port and address', ticket['granted'] and ticket['port'] == 7792 and
              ticket.get('address') == '203.0.113.20' and ticket['server'] == TRANSIT and len(ticket['ticket']) == 64)

        _, redeemed = call('POST', '/world/redeem', {'ticket': ticket['ticket'], 'server': CONCORD})
        check('another server cannot redeem it', not redeemed['granted'])
        _, redeemed = call('POST', '/world/redeem', {'ticket': ticket['ticket'], 'server': TRANSIT})
        check('Transit Row redeems it: whose and where', redeemed['granted'] and redeemed['account'] == 'acct-ash' and
              redeemed['character'] == 'Ash' and redeemed['arrival'] == 'portal-south' and redeemed['from'] == CONCORD)
        _, redeemed = call('POST', '/world/redeem', {'ticket': ticket['ticket'], 'server': TRANSIT})
        check('a ticket is used once', not redeemed['granted'])
        _, redeemed = call('POST', '/world/redeem', {'ticket': '0' * 64, 'server': TRANSIT})
        check('a made-up ticket is refused', not redeemed['granted'])

        _, first = call('POST', '/world/ticket', {'account': 'acct-ash', 'from': CONCORD, 'toMap': 'MeridianSprawl_Z1_TransitRow'})
        _, second = call('POST', '/world/ticket', {'account': 'acct-ash', 'from': CONCORD, 'toMap': 'MeridianSprawl_Z1_TransitRow'})
        _, redeemed = call('POST', '/world/redeem', {'ticket': first['ticket'], 'server': TRANSIT})
        check('a new ticket cancels the player\'s last one', second['granted'] and not redeemed['granted'])

        status, _ = call('POST', '/world/gone', {'server': TRANSIT})
        _, servers = call('GET', '/world/servers')
        check('a server that says it stops is gone at once', status == 200 and [s['map'] for s in servers] == ['Zone01_StarterTown'])

        print('  ... waiting 32 seconds for a ticket and a silent server to run out')
        report(TRANSIT, 'MeridianSprawl_Z1_TransitRow', 'Transit Row', 7792, [])
        _, late = call('POST', '/world/ticket', {'account': 'acct-ash', 'from': CONCORD, 'toMap': 'MeridianSprawl_Z1_TransitRow'})
        time.sleep(16)
        report(TRANSIT, 'MeridianSprawl_Z1_TransitRow', 'Transit Row', 7792, [])  # Transit Row keeps reporting; Concord falls silent
        time.sleep(16)
        _, redeemed = call('POST', '/world/redeem', {'ticket': late['ticket'], 'server': TRANSIT})
        check('a ticket runs out after 30 seconds', late['granted'] and not redeemed['granted'])
        _, servers = call('GET', '/world/servers')
        check('a server silent for 30 seconds is gone', [s['map'] for s in servers] == ['MeridianSprawl_Z1_TransitRow'])
    finally:
        service.kill()
    print('PASSED' if not failures else f'FAILED: {len(failures)} check(s)')
    return 0 if not failures else 1


if __name__ == '__main__':
    sys.exit(main())
