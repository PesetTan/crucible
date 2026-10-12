"""Check the app's longest-game aggregation without modifying its database."""
from pathlib import Path
import json
import re
import sqlite3

source = (Path(__file__).resolve().parents[1] / 'src/DeckTester/Store.cs').read_text()
method = source.split('public SessionTurnStats? SessionTurnStatistics()', 1)[1]
query = re.search(r'cmd.CommandText\s*=\s*"""(.*?)""";', method, re.S)[1]
db = sqlite3.connect(':memory:')
db.execute('CREATE TABLE slots(run_id TEXT, payload TEXT)')

def add(session, turns, status='completed'):
    payload = {'status': status, 'result': {'actualTurns': turns}}
    db.execute('INSERT INTO slots VALUES (?,?)', (session, json.dumps(payload)))

assert db.execute(query).fetchone() == (0, 0, None)
# Many shorter games must not outweigh the longest completed game.
for turns in [10] * 100 + [20]:
    add('large', turns)
add('small', 60)
add('failed', 999, 'failed')
add('running', 888, 'running')
add('invalid', 0)
assert db.execute(query).fetchone() == (2, 102, 60)
# A longer completed game increases the maximum.
add('large', 80)
assert db.execute(query).fetchone() == (2, 103, 80)
# A new shorter game cannot lower the automatic limit.
add('new', 30)
assert db.execute(query).fetchone() == (3, 104, 80)
add('new', 100)
assert db.execute(query).fetchone() == (3, 105, 100)
print('Longest-game checks passed: empty data, shorter games, exclusions, increasing maxima, and new sessions.')
