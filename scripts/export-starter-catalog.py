#!/usr/bin/env python3
"""Build a NEW allowlisted database. Never copy/delete tables from the account database."""
import argparse, gzip, hashlib, json, sqlite3, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA = {
    'cards': 'arena_id INTEGER PRIMARY KEY,title_id INTEGER,name TEXT,set_code TEXT,collector_number TEXT,mana_cost TEXT,mana_value INTEGER,type_line TEXT,rules_text TEXT,is_primary_card INTEGER,standard_legal INTEGER,linked_face_arena_ids TEXT,attributes TEXT',
    'card_catalog': 'arena_id INTEGER PRIMARY KEY,title_id INTEGER,name TEXT,set_code TEXT,collector_number TEXT,is_primary_card INTEGER',
    'untapped_decks': 'snapshot_id TEXT,deck_id TEXT,name TEXT,source_url TEXT,win_rate_percent REAL,matches_displayed TEXT,matches_approx INTEGER,archetype_tier TEXT,tier_source_url TEXT,tier_snapshot_date TEXT,PRIMARY KEY(snapshot_id,deck_id)',
    'untapped_deck_cards': 'snapshot_id TEXT,deck_id TEXT,zone TEXT,title_id INTEGER,arena_id INTEGER,quantity INTEGER',
    'decks': 'deck_id TEXT PRIMARY KEY,name TEXT,snapshot_time TEXT',
    'deck_cards': 'deck_id TEXT,zone TEXT,arena_id INTEGER,quantity INTEGER',
}
SNAPSHOTS = ['standard-bo1-bronze-platinum-2026-10-11', 'standard-bo1-tier-representatives-2026-10-11']

def verify(path):
    with sqlite3.connect(path.as_uri()+'?mode=ro',uri=True) as db:
        assert {r[0] for r in db.execute("SELECT name FROM sqlite_schema WHERE type='table'")} == set(SCHEMA)
        for table, definition in SCHEMA.items():
            # Compare against a schema we own, never a schema read from the private source.
            with sqlite3.connect(':memory:') as expected:
                expected.execute(f'CREATE TABLE {table}({definition})')
                assert list(db.execute(f'PRAGMA table_info({table})')) == list(expected.execute(f'PRAGMA table_info({table})'))
        assert db.execute('SELECT count(*) FROM decks').fetchone()[0] == 0
        assert db.execute('SELECT count(*) FROM deck_cards').fetchone()[0] == 0
        assert db.execute("SELECT count(*) FROM cards WHERE attributes!='{}'").fetchone()[0] == 0
        assert db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
        assert db.execute('SELECT count(*) FROM untapped_deck_cards d LEFT JOIN cards c USING(arena_id) WHERE c.arena_id IS NULL').fetchone()[0] == 0
        return {table: db.execute(f'SELECT count(*) FROM {table}').fetchone()[0] for table in SCHEMA}

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path)
    p.add_argument('--output',type=Path,default=ROOT/'resources/starter')
    p.add_argument('--verify',action='store_true')
    args=p.parse_args()
    if args.verify:
        manifest=json.loads((args.output/'manifest.json').read_text())
        raw=gzip.decompress((args.output/'catalog.sqlite.gz').read_bytes())
        assert hashlib.sha256(raw).hexdigest()==manifest['sha256']
        with tempfile.TemporaryDirectory() as tmp:
            target=Path(tmp)/'catalog.sqlite';target.write_bytes(raw)
            assert verify(target)==manifest['rows']
        print('Starter catalog checksum, schema, public rows, and privacy checks passed.');return
    if args.source is None:p.error('--source is required when exporting')
    with sqlite3.connect(args.source.resolve().as_uri()+'?mode=ro',uri=True) as source, tempfile.TemporaryDirectory() as tmp:
        target=Path(tmp)/'catalog.sqlite'
        with sqlite3.connect(target) as db:
            for table,definition in SCHEMA.items():
                db.execute(f'CREATE TABLE {table}({definition})')
                if table in ('decks','deck_cards'):continue
                columns=[r[1] for r in db.execute(f'PRAGMA table_info({table})')]
                selected=','.join("'{}'" if col=='attributes' else col for col in columns)
                where=' WHERE snapshot_id IN (?,?)' if table.startswith('untapped_') else ''
                rows=source.execute(f'SELECT {selected} FROM {table}{where} ORDER BY '+','.join(columns[:2]),SNAPSHOTS if where else []).fetchall()
                db.executemany(f'INSERT INTO {table} VALUES('+','.join('?' for _ in columns)+')',rows)
        rows=verify(target);raw=target.read_bytes()
    args.output.mkdir(parents=True,exist_ok=True)
    (args.output/'catalog.sqlite.gz').write_bytes(gzip.compress(raw,mtime=0))
    manifest={'version':1,'catalogDate':'2026-10-11','sha256':hashlib.sha256(raw).hexdigest(),'rows':rows,'snapshots':SNAPSHOTS,
        'provenance':'Card metadata from the local Arena catalog; public deck snapshots retain source URLs and dated published rates. No ownership is inferred. Custom annotations are excluded.',
        'generator':'scripts/export-starter-catalog.py'}
    (args.output/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print(json.dumps(rows))

if __name__=='__main__':main()
