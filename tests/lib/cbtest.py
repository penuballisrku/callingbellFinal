"""Shared helpers for the Calling Bell API test scripts (Python 3 standard library only).

Settings come from environment variables so the same scripts run against any local instance:
  CB_API        API base URL (default http://localhost:5080)
  CB_SQL        SQL Server instance (default .\\SQLEXPRESS)
  CB_DB         database name (default CallingBell)
  CB_PASSWORD   demo account password (default CallingBell@2026)
Every record a test creates carries TEST_MARK, so tests/sql/CleanupTestData.sql can remove it afterwards.
"""
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request

API = os.environ.get('CB_API', 'http://localhost:5080').rstrip('/')
SQL_SERVER = os.environ.get('CB_SQL', r'.\SQLEXPRESS')
DATABASE = os.environ.get('CB_DB', 'CallingBell')
PASSWORD = os.environ.get('CB_PASSWORD', 'CallingBell@2026')
TEST_MARK = '[cb-test]'

# Windows consoles default to a legacy code page; keep names like "Akhada" and ticks printable.
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')

_results = []


def call(method, path, body=None, token=None, raw=None, ctype='application/json', retries=2):
    """Calls the API and returns (status, parsed body, headers). Waits out a 429 (the 'auth' limit is 10 a minute per IP)."""
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    req = urllib.request.Request(API + path, data=data, method=method)
    if data is not None:
        req.add_header('Content-Type', ctype)
    if token:
        req.add_header('Authorization', 'Bearer ' + token)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            b = r.read()
            return r.status, (json.loads(b) if 'json' in r.headers.get('Content-Type', '') else b), r.headers
    except urllib.error.HTTPError as e:
        if e.code == 429 and retries > 0:
            wait = int(e.headers.get('Retry-After') or 60)
            print(f'  (rate limited on {path}; waiting {wait}s)')
            time.sleep(wait + 1)
            return call(method, path, body, token, raw, ctype, retries - 1)
        b = e.read()
        try:
            return e.code, json.loads(b), e.headers
        except ValueError:
            return e.code, b, e.headers
    except urllib.error.URLError as e:
        sys.exit(f'Cannot reach the API at {API} ({e.reason}). Start it first, or set CB_API.')


def login(email):
    st, r, _ = call('POST', '/api/auth/login', {'email': email, 'password': PASSWORD})
    if st != 200:
        sys.exit(f'Login failed for {email}: {st} {r}')
    return r['data']['accessToken']


def sql(query):
    """Runs a query with sqlcmd (Windows authentication) and returns the rows as lists of strings."""
    out = subprocess.run(['sqlcmd', '-S', SQL_SERVER, '-d', DATABASE, '-E', '-C', '-I', '-b', '-W', '-h', '-1', '-s', '|',
                          '-Q', 'SET NOCOUNT ON; ' + query], capture_output=True, text=True, encoding='utf-8')
    if out.returncode != 0:
        raise RuntimeError(out.stdout + out.stderr)
    return [line.split('|') for line in out.stdout.splitlines() if line.strip()]


def section(title):
    print(f'\n== {title} ==')


def check(name, ok, detail=''):
    _results.append(('PASS' if ok else 'FAIL', name))
    print(('PASS ' if ok else 'FAIL ') + name + (f'  [{detail}]' if detail != '' else ''))
    return ok


def skip(name, reason):
    _results.append(('SKIP', name))
    print(f'SKIP {name}  [{reason}]')


def info(text):
    print('     ' + text)


def finish():
    """Prints the summary and exits non-zero when anything failed (skips don't fail the run)."""
    passed = sum(s == 'PASS' for s, _ in _results)
    failed = [n for s, n in _results if s == 'FAIL']
    skipped = sum(s == 'SKIP' for s, _ in _results)
    print(f'\n{passed}/{passed + len(failed)} passed' + (f', {skipped} skipped' if skipped else ''))
    for n in failed:
        print('  failed: ' + n)
    sys.exit(1 if failed else 0)
