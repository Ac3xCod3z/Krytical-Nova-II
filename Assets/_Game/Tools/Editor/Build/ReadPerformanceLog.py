"""Reads a Krytical Nova II performance log and says what stands out.

    python ReadPerformanceLog.py                      the newest log of the Windows test build
    python ReadPerformanceLog.py <file or folder>     that log, or the newest in that folder
    python ReadPerformanceLog.py <file> --all         every slow frame, not only the worst

The game writes one log a run into PerformanceLogs beside its exe (PerformanceLogRecorder.cs). Lines are
tab-separated and start with their kind: S a second of play, F a slow frame, E something the game did,
C a change of settings, scene or session, # the header. Plain Python 3, no Unity needed.
"""
import os
import re
import sys
from collections import defaultdict

BUILD_LOGS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', '..',
                          'Builds', 'Windows', 'KryticalNovaII', 'PerformanceLogs')


def newest(folder):
    files = [os.path.join(folder, f) for f in os.listdir(folder) if f.startswith('perf_') and f.endswith('.txt')]
    if not files:
        raise SystemExit('no perf_*.txt in ' + folder)
    return max(files)  # the name carries the start time


def number(text):
    text = text.lstrip('~')
    try:
        return float(text)
    except ValueError:
        return None


def read(path):
    header, seconds, slow, events, changes = [], [], [], [], []
    with open(path, encoding='utf-8') as lines:
        for line in lines:
            cells = line.rstrip('\n').split('\t')
            kind = cells[0]
            if kind.startswith('#'):
                header.append(line.rstrip('\n'))
            elif kind == 'S' and len(cells) >= 20:
                seconds.append(dict(time=float(cells[1]), scene=cells[2], frames=int(cells[3]), mean=float(cells[4]),
                                    slowest=float(cells[5]), over20=int(cells[6]), main=number(cells[7]), render=number(cells[8]),
                                    gpu=number(cells[9]), setpass=int(cells[10]), triangles=int(cells[11]), memory=int(cells[12]),
                                    scripts=int(cells[13]), collections=int(cells[14]), players=int(cells[15]),
                                    objects=int(cells[16]), cost=float(cells[19])))
            elif kind == 'F' and len(cells) >= 12:
                phases = {}
                for part in cells[10].split(' '):
                    if '=' in part:
                        name, value = part.split('=', 1)
                        phases[name] = float(value)
                slow.append(dict(time=float(cells[1]), frame=int(cells[2]), ms=float(cells[3]), gc=cells[4] == 'gc',
                                 loading=cells[5] == 'loading', main=number(cells[6]), wait=number(cells[7]),
                                 render=number(cells[8]), gpu=number(cells[9]), unsure=cells[6].startswith('~'),
                                 phases=phases, before=cells[11]))
            elif kind == 'E' and len(cells) >= 6:
                events.append(dict(time=float(cells[1]), name=cells[2], ms=number(cells[3]), detail=cells[4], number=cells[5]))
            elif kind == 'C' and len(cells) >= 4:
                changes.append(dict(time=float(cells[1]), what=cells[2], detail=cells[3]))
    return header, seconds, slow, events, changes


def cause(frame):
    """A slow frame's likeliest cause, from what the log holds: a marker right before it, else its largest part."""
    near = []
    for item in frame['before'].split(', '):
        match = re.match(r'^(.*?)(?: ([0-9.]+)ms)? @([0-9.]+)$', item)
        if not match:
            continue
        name, took, ago = match.group(1), match.group(2), float(match.group(3))
        took = float(took) if took else None
        # A piece of timed work that ended inside this frame and took a real share of it.
        if took is not None and ago * 1000.0 <= frame['ms'] and took >= max(2.0, frame['ms'] * 0.2):
            near.append((took, name))
        elif took is None and ago * 1000.0 <= frame['ms']:
            near.append((0.0, name))
    timed = [n for n in near if n[0] > 0]
    if timed:
        return 'work: ' + max(timed)[1].split(':')[0]
    if frame['gc']:
        return 'memory collection'
    if near:
        return 'after: ' + near[-1][1].split(':')[0]
    if frame['phases']:
        name, value = max(frame['phases'].items(), key=lambda pair: pair[1])
        if value >= frame['ms'] * 0.4:
            return 'phase: ' + name
    if frame['gpu'] is not None and frame['gpu'] >= frame['ms'] * 0.7:
        return 'graphics card'
    return 'unexplained'


def percentile(values, share):
    if not values:
        return 0.0
    values = sorted(values)
    return values[min(len(values) - 1, int(len(values) * share))]


def main():
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    show_all = '--all' in sys.argv
    target = arguments[0] if arguments else BUILD_LOGS
    path = newest(target) if os.path.isdir(target) else target
    header, seconds, slow, events, changes = read(path)

    print(os.path.abspath(path))
    for line in header[:4]:
        print(' ', line.replace('\t', '   '))
    if not seconds:
        print('No seconds were recorded: the game ran for less than one.')
        return

    play = [f for f in slow if not f['loading']]
    loading = [f for f in slow if f['loading']]
    frames = sum(s['frames'] for s in seconds)
    length = seconds[-1]['time']
    print()
    print('%d s recorded, %d frames, %.0f a second on average' % (length, frames, frames / max(1.0, length)))
    print('usual frame %.1f ms, one second in a hundred worse than %.1f ms mean'
          % (percentile([s['mean'] for s in seconds], 0.5), percentile([s['mean'] for s in seconds], 0.99)))
    over = [f for f in play if f['ms'] > 20.0]
    print('slow frames in play: %d over 20 ms (%.2f a minute), worst %.1f ms; while loading: %d'
          % (len(over), len(over) * 60.0 / max(1.0, length), max([f['ms'] for f in play] or [0.0]), len(loading)))
    costs = [s['cost'] for s in seconds]
    print('the log itself cost %.1f microseconds a frame' % (sum(costs) / len(costs)))

    if changes:
        print()
        print('CHANGES (numbers before and after one are not to be compared)')
        for change in changes:
            print('  %8.1f s  %-9s %s' % (change['time'], change['what'], change['detail']))

    if play:
        print()
        print('SLOW FRAMES IN PLAY, BY LIKELIEST CAUSE')
        groups = defaultdict(list)
        for frame in play:
            groups[cause(frame)].append(frame)
        for name, group in sorted(groups.items(), key=lambda pair: -sum(f['ms'] for f in pair[1])):
            print('  %-28s %4d frames, worst %6.1f ms, mean %5.1f ms' % (name, len(group), max(f['ms'] for f in group), sum(f['ms'] for f in group) / len(group)))

        print()
        print('THE WORST' if not show_all else 'EVERY SLOW FRAME IN PLAY')
        chosen = play if show_all else sorted(play, key=lambda f: -f['ms'])[:15]
        for frame in sorted(chosen, key=lambda f: f['time']):
            phases = ' '.join('%s=%.1f' % pair for pair in sorted(frame['phases'].items(), key=lambda pair: -pair[1])[:4])
            split = 'main %s gpu %s' % ('-' if frame['main'] is None else '%.1f' % frame['main'], '-' if frame['gpu'] is None else '%.1f' % frame['gpu'])
            print('  %8.2f s  %6.1f ms  %s%s | %s | %s' % (frame['time'], frame['ms'], 'gc ' if frame['gc'] else '', split + (' (unsure)' if frame['unsure'] else ''), phases, frame['before'][:150]))

    timed = defaultdict(list)
    for event in events:
        if event['ms'] is not None:
            timed[event['name']].append(event['ms'])
    if timed:
        print()
        print('TIMED WORK (every time it ran, slow frame or not)')
        for name, values in sorted(timed.items(), key=lambda pair: -max(pair[1])):
            print('  %-18s %5d times, worst %7.2f ms, mean %6.2f ms, in all %8.1f ms' % (name, len(values), max(values), sum(values) / len(values), sum(values)))

    print()
    print('OVER THE SESSION (first minute -> last minute)')
    first, last = seconds[:60], seconds[-60:]

    def mean(rows, key):
        values = [r[key] for r in rows if r[key] is not None]
        return sum(values) / len(values) if values else 0.0

    for label, key in (('frame ms', 'mean'), ('main thread ms', 'main'), ('render thread ms', 'render'), ('GPU ms', 'gpu'),
                       ('set-pass calls', 'setpass'), ('triangles', 'triangles'), ('memory MB', 'memory'),
                       ('script memory MB', 'scripts'), ('networked objects', 'objects')):
        print('  %-18s %12.1f -> %12.1f' % (label, mean(first, key), mean(last, key)))
    print('  %-18s %12d in all (%.1f a minute)' % ('collections', seconds[-1]['collections'] - seconds[0]['collections'],
                                                (seconds[-1]['collections'] - seconds[0]['collections']) * 60.0 / max(1.0, length)))


if __name__ == '__main__':
    main()
