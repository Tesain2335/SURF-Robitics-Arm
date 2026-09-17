"""Estimate discrete move-and-hold latency from Unity's local-clock CSV.

Uses link6 FK from real feedback, not a measured tool-tip position. Thresholds:
target onset 3 mm, response 2 mm, settled within 10 mm for 150 ms.
This estimates latency after Unity has sampled the controller, including the
feedback return path. It does not measure headset tracking's internal delay.
"""
import csv
import json
import math
import sys
from pathlib import Path


def analyze(rows):
    distance = math.dist
    trials = []
    baseline = None
    active = None
    for row in rows:
        t = float(row['unity_time_s'])
        target = tuple(float(row['target_' + axis]) for axis in 'xyz')
        actual = tuple(float(row['feedback_' + axis]) for axis in 'xyz')
        if int(row['command_valid']) != 1 or float(row['feedback_age_s']) >= .5:
            baseline = active = None
            continue
        if baseline is None:
            baseline = (t, target, actual)
            continue
        if active is None:
            if distance(target, baseline[1]) < .003:
                continue
            if t - baseline[0] < .3:
                baseline = (t, target, actual)
                continue
            active = dict(start=t, initial=baseline[1], actual=baseline[2],
                          last_target=target, last_change=t, response=None, in_band=None)
        a = active
        if distance(target, a['last_target']) >= .002:
            a['last_change'] = t
            a['last_target'] = target
            a['in_band'] = None
        if a['response'] is None and distance(actual, a['actual']) >= .002:
            a['response'] = t - a['start']
        if distance(actual, target) <= .01:
            if a['in_band'] is None:
                a['in_band'] = t
        else:
            a['in_band'] = None
        settled = (a['in_band'] is not None and t-a['in_band'] >= .15
                   and t-a['last_change'] >= .2)
        if settled or t-a['start'] > 8:
            if distance(target, a['initial']) >= .015:
                arrival = max(0, a['in_band']-a['last_change']) if settled else None
                trials.append(dict(start_s=a['start'], displacement_m=distance(target,a['initial']),
                    response_s=a['response'], arrival_after_target_stop_s=arrival,
                    total_from_target_onset_s=a['in_band']-a['start'] if settled else None,
                    under_1s=(a['response'] is not None and a['response']<1
                              and arrival is not None and arrival<1)))
            baseline = (t, target, actual)
            active = None
    return trials


if __name__ == '__main__':
    file = Path(sys.argv[1])
    with file.open(encoding='utf-8-sig', newline='') as stream:
        trials = analyze(list(csv.DictReader(stream)))
    print(json.dumps({'file': str(file), 'trials': trials,
        'note': 'Move-and-hold estimates; no trials means insufficient data, not a pass. Includes feedback return latency, excludes headset internal tracking delay.'}, indent=2))
