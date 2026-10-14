#!/usr/bin/env python3
"""Append extensible JSON attributes to local card entries; keep an audit history."""
import argparse
import datetime
import json
from pathlib import Path
import sqlite3

def merge(old, patch, replace=False):
    out=json.loads(json.dumps(old))
    for key,value in patch.items():
        if key not in out: out[key]=value
        elif isinstance(out[key],dict) and isinstance(value,dict): out[key]=merge(out[key],value,replace)
        elif isinstance(out[key],list) and isinstance(value,list):
            if replace:out[key]=value;continue
            for item in value:
                if item not in out[key]:out[key].append(item)
        elif out[key]==value:pass
        elif replace:out[key]=value
        else:raise ValueError(f"Attribute {key!r} conflicts; use --replace-existing explicitly")
    return out

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--db',default=str(Path(__file__).resolve().parents[3]/'standard_cards.sqlite'))
    p.add_argument('--seed',type=Path)
    p.add_argument('--name')
    p.add_argument('--arena-id',type=int)
    p.add_argument('--json',dest='patch')
    p.add_argument('--replace-existing',action='store_true')
    a=p.parse_args()
    db=sqlite3.connect(a.db,timeout=30)
    if 'attributes' not in {r[1] for r in db.execute('pragma table_info(cards)')}:
        with sqlite3.connect(a.db+'.before-attributes.sqlite') as backup:db.backup(backup)
        db.execute("ALTER TABLE cards ADD COLUMN attributes TEXT NOT NULL DEFAULT '{}' CHECK(json_valid(attributes) AND json_type(attributes)='object')")
    db.execute('CREATE TABLE IF NOT EXISTS card_attributes_history(revision INTEGER PRIMARY KEY AUTOINCREMENT,arena_id INTEGER NOT NULL,changed_at TEXT NOT NULL,before_json TEXT NOT NULL,after_json TEXT NOT NULL)')
    patches=json.loads(a.seed.read_text()) if a.seed else {a.name or a.arena_id:json.loads(a.patch or '{}')}
    changed=0
    with db:
        for name,patch in patches.items():
            if not isinstance(patch,dict):raise ValueError('attributes must be an object')
            rows=list(db.execute('SELECT arena_id,attributes FROM cards WHERE '+('arena_id=?' if isinstance(name,int) else 'name=?'),(name,)))
            if not rows:raise ValueError(f'Card not found: {name}')
            for arena_id,before in rows:
                after=json.dumps(merge(json.loads(before),patch,a.replace_existing),ensure_ascii=False,sort_keys=True,separators=(',',':'))
                if json.loads(before)==json.loads(after):continue
                db.execute('UPDATE cards SET attributes=? WHERE arena_id=?',(after,arena_id))
                db.execute('INSERT INTO card_attributes_history(arena_id,changed_at,before_json,after_json) VALUES(?,?,?,?)',(arena_id,datetime.datetime.now(datetime.timezone.utc).isoformat(),before,after))
                changed+=1
    print(json.dumps({'updatedCardEntries':changed,'database':a.db}))

if __name__=='__main__':main()
