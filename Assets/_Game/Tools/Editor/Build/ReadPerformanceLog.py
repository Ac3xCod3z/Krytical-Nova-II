"""Reads a Krytical Nova II performance log and says what stands out.

    python ReadPerformanceLog.py                      the newest log of the Windows test build
    python ReadPerformanceLog.py <file or folder>     that log, or the newest in that folder
    python ReadPerformanceLog.py <file> --all         every slow frame, not only the worst

The game writes one log a run into PerformanceLogs beside its exe (PerformanceLogRecorder.cs). Lines are
tab-separated and start with their kind: S a second of play, F a slow frame, E something the game did,
C a change of settings, scene or session, H how long every frame of a section took (a count for each
length), # the header. Plain Python 3, no Unity needed.

The mark a section is held to is the owner's (10 Oct 2026): a locked 60 frames a second, and no frame of
play under 50 (over 20 ms), even as a spike. An average does not count.
"""
import os
import re
import sys
from collections import defaultdict

BUILD_LOGS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..', '..',
                          'Builds', 'Windows', 'KryticalNovaII', 'PerformanceLogs')
FLOOR_MS = 20.0          # no frame of play may take longer: 50 a second
LOCKED_MS = 1000.0 / 60  # and the slowest hundredth of them must still make 60
# Work the log times that is not the frame's: it runs on a thread of its own.
OFF_THE_FRAME = ('save.disk',)


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


def pairs(text):
    """ "A=1.5 B=0.2" as a dictionary."""
    found = {}
    for part in text.split(' '):
        if '=' in part:
            name, value = part.rsplit('=', 1)
            try:
                found[name] = float(value)
            except ValueError:
                pass
    return found


def read(path):
    header, seconds, slow, events, changes, sections = [], [], [], [], [], {}
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
                                    objects=int(cells[16]), cost=float(cells[19]),
                                    # Logs from before 18 Oct 2026 have no batches or draw calls.
                                    batches=int(cells[20]) if len(cells) >= 22 else None,
                                    draws=int(cells[21]) if len(cells) >= 22 else None))
            elif kind == 'F' and len(cells) >= 12:
                # Logs from before 11 Oct 2026 have no column of steps.
                steps = pairs(cells[12]) if len(cells) >= 13 else {}
                slow.append(dict(time=float(cells[1]), frame=int(cells[2]), ms=float(cells[3]), gc=cells[4] == 'gc',
                                 loading=cells[5] == 'loading', main=number(cells[6]), wait=number(cells[7]),
                                 render=number(cells[8]), gpu=number(cells[9]), unsure=cells[6].startswith('~'),
                                 phases=pairs(cells[10]), before=cells[11],
                                 steps={k: v for k, v in steps.items() if not k.startswith('camera:')},
                                 cameras={k[7:]: v for k, v in steps.items() if k.startswith('camera:')}))
            elif kind == 'E' and len(cells) >= 6:
                events.append(dict(time=float(cells[1]), name=cells[2], ms=number(cells[3]), detail=cells[4], number=cells[5]))
            elif kind == 'C' and len(cells) >= 4:
                changes.append(dict(time=float(cells[1]), what=cells[2], detail=cells[3]))
            elif kind == 'H' and len(cells) >= 10:
                bins = {}
                for part in cells[9].split(' '):
                    if ':' in part:
                        start, count = part.split(':', 1)
                        bins[float(start)] = int(count)
                # A section's last line is the whole of it: a later one of the same number replaces the earlier.
                sections[int(cells[2])] = dict(serial=int(cells[2]), time=float(cells[1]), name=cells[3], frames=int(cells[4]), seconds=float(cells[5]),
                                               slowest=float(cells[6]), over20=int(cells[7]), loading=int(cells[8]), bins=bins)
    return header, seconds, slow, events, changes, [sections[key] for key in sorted(sections)]


def cause(frame):
    """A slow frame's likeliest cause, from what the log holds: a marker right before it, else its largest part."""
    near = []
    for item in frame['before'].split(', '):
        match = re.match(r'^(.*?)(?: ([0-9.]+)ms)? @([0-9.]+)$', item)
        if not match:
            continue
        name, took, ago = match.group(1), match.group(2), float(match.group(3))
        if name.split(':')[0] in OFF_THE_FRAME:
            continue
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
    # The step that took most of it, when one did: it names more than its phase does.
    if frame['steps']:
        name, value = max(frame['steps'].items(), key=lambda pair: pair[1])
        if value >= frame['ms'] * 0.4:
            camera = max(frame['cameras'].items(), key=lambda pair: pair[1]) if frame['cameras'] else None
            if camera and camera[1] >= value * 0.6:
                return 'camera: ' + camera[0]
            return 'step: ' + name
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


def bin_width(start):
    return 0.25 if start < 50.0 else 5.0


def slowest_share(bins, frames, share):
    """The mean length of the slowest share of the frames (0.01 = the "1 % low"), each taken at the middle of its bin."""
    wanted = max(1.0, frames * share)
    left, total = wanted, 0.0
    for start in sorted(bins, reverse=True):
        take = min(left, bins[start])
        total += take * (start + bin_width(start) * 0.5)
        left -= take
        if left <= 0:
            break
    return total / (wanted - left) if wanted > left else 0.0


def handed_out(section, seconds, changes):
    """What the scripts were handed during a section, in KB a frame, and the collections that ran in it."""
    starts = [0.0] + [c['time'] for c in changes if c['what'] == 'section']  # a section's number is its place in this list
    serial = section['serial']
    if serial >= len(starts):
        return None, None
    start = starts[serial]
    end = starts[serial + 1] if serial + 1 < len(starts) else float('inf')
    rows = [s for s in seconds if start < s['time'] <= end]
    if len(rows) < 2:
        return None, None
    handed = sum(max(0, b['scripts'] - a['scripts']) for a, b in zip(rows, rows[1:]))
    frames = sum(r['frames'] for r in rows[1:])
    return handed * 1024.0 / max(1, frames), rows[-1]['collections'] - rows[0]['collections']


def print_sections(sections, seconds, changes):
    print()
    print('SECTIONS (every frame of play counted, not only the slow ones; frames while loading left out)')
    print('  %-22s %8s %8s %9s %9s %10s %9s %9s %11s  %s' % ('section', 'seconds', 'average', '1 % low', '0.1 % low', 'slowest ms', 'over 20',
                                                           'KB/frame', 'collections', 'mark'))
    for section in sections:
        if section['frames'] == 0:
            continue
        garbage, collections = handed_out(section, seconds, changes)
        garbage = '-' if garbage is None else '%.1f' % garbage
        collections = '-' if collections is None else '%d' % collections
        low = slowest_share(section['bins'], section['frames'], 0.01)
        lowest = slowest_share(section['bins'], section['frames'], 0.001)
        if section['over20'] > 0:
            mark = 'FAILS: %d frame(s) under 50 a second' % section['over20']
        elif low > LOCKED_MS:
            mark = 'FAILS: the 1 %% low is under 60 (%.0f)' % (1000.0 / low)
        else:
            # How much slower a PC could be and still pass, by the worst frame alone.
            mark = 'passes; room for a PC %.1f times slower by the worst frame' % (FLOOR_MS / max(0.01, section['slowest']))
        print('  %-22s %8.0f %8.0f %9.0f %9.0f %10.1f %9d %9s %11s  %s'
              % (section['name'][:22], section['seconds'], section['frames'] / max(0.001, section['seconds']),
                 1000.0 / max(0.01, low), 1000.0 / max(0.01, lowest), section['slowest'], section['over20'], garbage, collections, mark))
    print('  (frames a second; a "low" is the mean of the slowest 1 % / 0.1 % of the frames; the mark is 60 locked, none under 50;')
    print('   KB/frame is the memory the scripts were handed, which the collections are for: the first section holds the game starting up)')


def main():
    arguments = [a for a in sys.argv[1:] if not a.startswith('--')]
    show_all = '--all' in sys.argv
    target = arguments[0] if arguments else BUILD_LOGS
    path = newest(target) if os.path.isdir(target) else target
    header, seconds, slow, events, changes, sections = read(path)

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
    over = [f for f in play if f['ms'] > FLOOR_MS]
    print('slow frames in play: %d over 20 ms (%.2f a minute), worst %.1f ms; while loading: %d'
          % (len(over), len(over) * 60.0 / max(1.0, length), max([f['ms'] for f in play] or [0.0]), len(loading)))
    costs = [s['cost'] for s in seconds]
    print('the log itself cost %.1f microseconds a frame' % (sum(costs) / len(costs)))

    if sections:
        print_sections(sections, seconds, changes)

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
            print('  %-46s %4d frames, worst %6.1f ms, mean %5.1f ms' % (name, len(group), max(f['ms'] for f in group), sum(f['ms'] for f in group) / len(group)))

        stepped = [f for f in play if f['steps']]
        if stepped:
            print()
            print('WHERE THEIR TIME WENT, BY STEP (every slow frame of play, whatever its cause; ms in all)')
            by_step, by_camera = defaultdict(float), defaultdict(float)
            for frame in stepped:
                for name, value in frame['steps'].items():
                    by_step[name] += value
                for name, value in frame['cameras'].items():
                    by_camera[name] += value
            whole = sum(f['ms'] for f in stepped)
            for name, value in sorted(by_step.items(), key=lambda pair: -pair[1])[:12]:
                print('  %-46s %9.1f ms  %4.0f %%' % (name, value, value * 100.0 / max(0.001, whole)))
            for name, value in sorted(by_camera.items(), key=lambda pair: -pair[1])[:6]:
                print('  %-46s %9.1f ms  %4.0f %%  (inside the step that draws)' % ('camera: ' + name, value, value * 100.0 / max(0.001, whole)))

        print()
        print('THE WORST' if not show_all else 'EVERY SLOW FRAME IN PLAY')
        chosen = play if show_all else sorted(play, key=lambda f: -f['ms'])[:15]
        for frame in sorted(chosen, key=lambda f: f['time']):
            parts = frame['steps'] if frame['steps'] else frame['phases']
            where = ' '.join('%s=%.1f' % pair for pair in sorted(parts.items(), key=lambda pair: -pair[1])[:4])
            where += ''.join(' camera:%s=%.1f' % pair for pair in sorted(frame['cameras'].items(), key=lambda pair: -pair[1])[:2])
            split = 'main %s gpu %s' % ('-' if frame['main'] is None else '%.1f' % frame['main'], '-' if frame['gpu'] is None else '%.1f' % frame['gpu'])
            print('  %8.2f s  %6.1f ms  %s%s | %s | %s' % (frame['time'], frame['ms'], 'gc ' if frame['gc'] else '', split + (' (unsure)' if frame['unsure'] else ''), where, frame['before'][:150]))

    timed = defaultdict(list)
    for event in events:
        if event['ms'] is not None:
            timed[event['name']].append(event['ms'])
    if timed:
        print()
        print('TIMED WORK (every time it ran, slow frame or not)')
        for name, values in sorted(timed.items(), key=lambda pair: -max(pair[1])):
            note = '  (not the frame\'s: its own thread)' if name in OFF_THE_FRAME else ''
            print('  %-18s %5d times, worst %7.2f ms, mean %6.2f ms, in all %8.1f ms%s' % (name, len(values), max(values), sum(values) / len(values), sum(values), note))

    print()
    print('OVER THE SESSION (first minute -> last minute)')
    first, last = seconds[:60], seconds[-60:]

    def mean(rows, key):
        values = [r[key] for r in rows if r[key] is not None]
        return sum(values) / len(values) if values else 0.0

    for label, key in (('frame ms', 'mean'), ('main thread ms', 'main'), ('render thread ms', 'render'), ('GPU ms', 'gpu'),
                       ('set-pass calls', 'setpass'), ('batches', 'batches'), ('draw calls', 'draws'), ('triangles', 'triangles'), ('memory MB', 'memory'),
                       ('script memory MB', 'scripts'), ('networked objects', 'objects')):
        if all(r[key] is None for r in seconds):
            print('  %-18s %12s' % (label, 'not in log'))
            continue
        print('  %-18s %12.1f -> %12.1f' % (label, mean(first, key), mean(last, key)))
    collections = seconds[-1]['collections'] - seconds[0]['collections']
    print('  %-18s %12d in all (%.1f a minute)' % ('collections', collections, collections * 60.0 / max(1.0, length)))
    # What the scripts were handed between collections: every rise of script memory from one second to the next.
    handed = sum(max(0, b['scripts'] - a['scripts']) for a, b in zip(seconds, seconds[1:]))
    print('  %-18s %12.1f MB a minute handed out to scripts over the whole run, starting up included (%.1f KB a frame); by section above'
          % ('garbage', handed * 60.0 / max(1.0, length), handed * 1024.0 / max(1, frames)))


if __name__ == '__main__':
    main()
