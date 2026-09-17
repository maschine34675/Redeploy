"""Tabulates Redeploy.Core's "Respawn pipeline" log lines (Core 1.0.2+) and ArenaMode's
"Arena respawn timing" lines from one BepInEx log, then fits the growing stages against minutes into
the raid, managed heap and respawn number (docs/RESPAWN-API.md, header block "Messraid 17.09.2026").

    python tools/pipeline-timing/analyze.py BepInEx/LogOutput.log

A respawn-count-driven cost fits the respawn number best; a time- or scene-driven one jumps across a
pause without respawns. Standard library only.
"""
import re, sys
sys.stdout.reconfigure(encoding='utf-8')
path = sys.argv[1]
lines = open(path, encoding='utf-8', errors='replace').read().splitlines()

def secs(t):
    h, m, s = t.split(':')
    return int(h) * 3600 + int(m) * 60 + float(s)

rows = {}
arena = []
for line in lines:
    m = re.match(r'\[([\d:.]+)\].*Respawn pipeline #(\d+) \(([^,]+), ([^)]+)\) on \w+: ([\d.]+) ms in (\d+) frame\(s\) \| (.*) \|\| restore ([\d.]+) ms \(([-\d]+) victims\) \| before \(read in ([\d.]+) ms\): (.*)$', line)
    if m:
        t, idx, reason, outcome, total, frames, stages, restore, victims, readms, before = m.groups()
        idx = int(idx)
        r = rows.setdefault(idx, {})
        r.update(t=t, reason=reason, outcome=outcome, total=float(total), frames=int(frames), restore=float(restore), read=float(readms))
        for part in stages.split(' | '):
            g = re.match(r'(\w+) ([\d.]+)\*?(?: \((.*)\))?$', part)
            if not g:
                continue
            r['g.' + g.group(1)] = float(g.group(2))
            if g.group(3):
                for step in g.group(3).split(', '):
                    s = re.match(r'(\w+) ([\d.]+)', step)
                    if s:
                        r[g.group(1) + '.' + s.group(1)] = float(s.group(2))
        for kv in before.split(', '):
            k, _, v = kv.partition(' ')
            v = v.replace(' ms', '').replace(' MB', '')
            try:
                r['b.' + k] = float(v)
            except ValueError:
                pass
        continue
    m = re.match(r'\[([\d:.]+)\].*Respawn pipeline #(\d+) after: ([\d.]+) ms to .*next frame ([\d.]+) ms(?:, active LocalPlayer objects (\d+) \(counted in ([\d.]+) ms\))?', line)
    if m:
        r = rows.setdefault(int(m.group(2)), {})
        r['rest'] = float(m.group(3)); r['next'] = float(m.group(4))
        if m.group(5):
            r['players'] = int(m.group(5)); r['countms'] = float(m.group(6))
        continue
    m = re.match(r'\[([\d:.]+)\].*Arena respawn timing: kit ([\d.]+)s, bundles ([\d.]+)s, prepare ([\d.]+)s, pipeline ([\d.]+)s, total ([\d.]+)s', line)
    if m:
        arena.append((secs(m.group(1)), float(m.group(5)), float(m.group(3))))

start = secs(rows[1]['t'])
# attach arena pipeline to the nearest following core line within 2 s
for idx, r in rows.items():
    if 't' not in r:
        continue
    ts = secs(r['t'])
    r['min'] = (ts - start) / 60
    for at, pipe, bundles in arena:
        if 0 <= at - ts < 2:
            r['arena'] = pipe * 1000; r['bundles'] = bundles
            break

cols = [('min', '{:5.1f}'), ('total', '{:6.1f}'), ('rewire.freecam', '{:6.1f}'), ('rewire.camera', '{:6.1f}'), ('rewire.ownerStart', '{:5.1f}'),
        ('g.spawn', '{:5.1f}'), ('g.create', '{:5.1f}'), ('rest', '{:6.1f}'), ('next', '{:5.1f}'), ('arena', '{:6.0f}'),
        ('b.heap', '{:6.0f}'), ('b.frame', '{:5.1f}'), ('b.environmentHandlers', '{:4.0f}'), ('b.skillHandlers', '{:4.0f}'), ('b.cullingJobs', '{:3.0f}'), ('players', '{:4}'), ('countms', '{:6}')]
print('#  ' + ' '.join(c[0].split('.')[-1][:8].rjust(8) for c in cols))
for idx in sorted(rows):
    r = rows[idx]
    out = []
    for key, fmt in cols:
        v = r.get(key)
        out.append((fmt.format(v) if v is not None else '-').rjust(8))
    print(f'{idx:<3}' + ' '.join(out))

# simple least squares against respawn index and minutes, excluding #1 (raid start swap)
def fit(xs, ys):
    n = len(xs); mx = sum(xs) / n; my = sum(ys) / n
    sxx = sum((x - mx) ** 2 for x in xs); sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    slope = sxy / sxx if sxx else 0
    ss_res = sum((y - (my + slope * (x - mx))) ** 2 for x, y in zip(xs, ys)); ss_tot = sum((y - my) ** 2 for y in ys)
    return slope, (1 - ss_res / ss_tot) if ss_tot else 0

keys = ['total', 'rewire.freecam', 'rewire.camera', 'rest', 'arena', 'g.create', 'rewire.ownerStart', 'next']
sel = [i for i in sorted(rows) if i >= 3 and all(k in rows[i] for k in ('min', 'total'))]
print()
for k in keys:
    pts = [(rows[i]['min'], rows[i]['b.heap'], i, rows[i][k]) for i in sel if k in rows[i]]
    if len(pts) < 3:
        continue
    s_t, r2_t = fit([p[0] for p in pts], [p[3] for p in pts])
    s_h, r2_h = fit([p[1] for p in pts], [p[3] for p in pts])
    s_i, r2_i = fit([p[2] for p in pts], [p[3] for p in pts])
    print(f'{k:20} per minute {s_t:7.2f} ms (R2 {r2_t:.2f}) | per GB heap {s_h*1024:7.2f} ms (R2 {r2_h:.2f}) | per respawn {s_i:6.2f} ms (R2 {r2_i:.2f})')
