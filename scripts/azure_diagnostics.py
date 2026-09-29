"""Read-only Azure diagnostics. Never read app settings, credentials or app logs."""

import datetime as dt
import json
import re
import subprocess
from urllib.parse import urlencode


def emit(label, value):
    print(label + ': ' + json.dumps(value, ensure_ascii=False), flush=True)


def az_json(label, *args):
    try:
        result = subprocess.run(
            ['az', *args, '--output', 'json', '--only-show-errors'],
            capture_output=True, text=True, timeout=55,
        )
    except subprocess.TimeoutExpired:
        emit(label, {'available': False, 'reason': 'timeout'})
        return None
    if result.returncode:
        # Only classify errors. Never export raw CLI diagnostics or payloads.
        reason = 'request failed'
        for marker in ('AuthorizationFailed', 'Forbidden', 'NotFound', 'BadRequest', 'InvalidRequest'):
            if marker.casefold() in result.stderr.casefold():
                reason = marker
                break
        emit(label, {'available': False, 'reason': reason})
        return None
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError:
        emit(label, {'available': False, 'reason': 'non-JSON response'})
        return None


def get(label, path, **query):
    return az_json(label, 'rest', '--method', 'get', '--url',
                   'https://management.azure.com' + path + '?' + urlencode(query))


def fields(data, *keys):
    return {key: data.get(key) for key in keys}


account = az_json('account', 'account', 'show')
if not account:
    raise SystemExit('No Azure session')
emit('subscription', fields(account, 'state'))
app_id = f"/subscriptions/{account['id']}/resourceGroups/asp/providers/Microsoft.Web/sites/VPFAlgorithmBot"
app = get('app', app_id, **{'api-version': '2025-05-01'})
if not app:
    raise SystemExit('App resource unavailable')
props = app.get('properties') or {}
emit('timestampUtc', dt.datetime.now(dt.timezone.utc).isoformat())
emit('app', fields(props, 'state', 'usageState', 'enabled', 'availabilityState',
                   'runtimeAvailabilityState', 'contentAvailabilityState', 'suspendedTill',
                   'dailyMemoryTimeQuota', 'lastModifiedTimeUtc'))

plan_id = props.get('serverFarmId')
if plan_id:
    plan = get('plan', plan_id, **{'api-version': '2025-05-01'})
    if plan:
        emit('plan', {'sku': fields(plan.get('sku') or {}, 'name', 'tier', 'capacity'),
                      **fields(plan.get('properties') or {}, 'status', 'numberOfSites',
                               'maximumNumberOfWorkers', 'isSpot', 'freeOfferExpirationTime')})

usages = get('quotas', app_id + '/usages', **{'api-version': '2025-05-01'})
if usages is not None:
    emit('quotas', [{'name': (item.get('name') or {}).get('value'),
                     **fields(item, 'currentValue', 'limit', 'unit', 'nextResetTime')}
                    for item in usages.get('value', [])])

now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
day_start = now.replace(hour=0, minute=0, second=0)
def iso(value):
    return value.isoformat().replace('+00:00', 'Z')

for metric, aggregation in [('CpuTime', 'Total'), ('MemoryWorkingSet', 'Average'),
                             ('Requests', 'Total'), ('Http403', 'Total')]:
    data = get('metric ' + metric, app_id + '/providers/microsoft.insights/metrics', **{
        'api-version': '2018-01-01', 'metricnames': metric, 'aggregation': aggregation,
        'interval': 'PT5M', 'timespan': iso(day_start) + '/' + iso(now),
    })
    if data is None:
        continue
    if not data.get('value'):
        emit('metric ' + metric, {'available': False, 'reason': 'no metric data returned'})
    for item in data.get('value', []):
        values = []
        missing = 0
        for series in item.get('timeseries', []):
            for point in series.get('data', []):
                value = point.get(aggregation.lower())
                if isinstance(value, (int, float)):
                    values.append({'utc': point.get('timeStamp'), 'value': value})
                else:
                    missing += 1
        emit('metric ' + metric, {'unit': item.get('unit'), 'errorCode': item.get('errorCode'),
                                  'reportedPoints': len(values), 'missingPoints': missing,
                                  'sum' if aggregation == 'Total' else 'peak':
                                      (sum(p['value'] for p in values) if aggregation == 'Total'
                                       else max((p['value'] for p in values), default=None)),
                                  'nonzeroIntervals': [p for p in values if p['value'] != 0][-96:]})

# Discover platform detector IDs without exporting detector results or application logs.
detectors = get('detector catalog', app_id + '/detectors', **{'api-version': '2025-05-01'})
if detectors:
    selected = []
    for item in detectors.get('value', []):
        info = (item.get('properties') or {}).get('metadata') or {}
        detector_id = info.get('id') or item.get('name')
        title = info.get('name') or ''
        if re.search(r'quota|free|stop|limit|restart|crash|linux.*(cpu|memory|down)|app.?down|availability', str(detector_id) + ' ' + title, re.I):
            selected.append({'id': detector_id, 'title': title})
    emit('relevantDetectors', selected)

for detector_id in ('LinuxAppDown', 'LinuxAppDown2'):
    result = get('detector ' + detector_id, app_id + '/detectors/' + detector_id, **{
        'api-version': '2025-05-01', 'startTime': iso(day_start), 'endTime': iso(now),
    })
    if not result:
        continue
    # Detector output can contain application log text. Export only schema metadata,
    # numeric facts and fixed diagnostic classifications, never original text cells.
    markers = ('quota', 'wpstoprequests', 'restart', 'crash', 'cpu', 'memory', 'disk',
               'storage', 'hour', 'minute', 'sql', 'authentication', 'start limit',
               'container exit', 'container failed', 'unhealthy', 'blocked')
    causes = ('SqlException', 'Failed to authenticate', 'ActiveDirectoryIntegrated',
              'Login failed', 'CREATE TABLE permission denied', 'CREATE VIEW permission denied',
              'CREATE SCHEMA permission denied', 'Cannot find the schema', 'does not exist',
              'already exists', 'Cannot open server', 'is not allowed to access the server',
              'Cannot open database', 'network-related', 'TCP Provider', 'timeout expired',
              'OutOfMemory', 'OOM', 'exit code 137', 'exit code 134', 'exit code 1',
              'did not respond', "didn't respond", 'failed to start', 'quota exceeded',
              'UnauthorizedAccessException', 'no space left on device', '{your_username}')
    for index, dataset in enumerate((result.get('properties') or {}).get('dataset') or []):
        table = dataset.get('table') or {}
        columns = [col.get('columnName') for col in table.get('columns', [])]
        rows = table.get('rows') or []
        summaries = []
        for row_index, row in enumerate(rows):
            row_text = json.dumps(row).casefold()
            matches = [marker for marker in markers if marker in row_text]
            if not matches:
                continue
            numeric = {}
            for column, value in zip(columns, row):
                if re.search(r'quota|limit|count|restart|time|value|status', str(column), re.I):
                    if isinstance(value, (int, float)) or re.fullmatch(r'-?\d+(\.\d+)?', str(value)):
                        numeric[column] = value
                    elif re.fullmatch(r'\d{4}-\d{2}-\d{2}[T ][\d:.+Z-]+', str(value)):
                        numeric[column] = value
            summaries.append({'row': row_index, 'markers': matches, 'numeric': numeric,
                              'causeMarkers': [cause for cause in causes if cause.casefold() in row_text],
                              'utcTimes': sorted(set(re.findall(r'\b2026-\d{2}-\d{2}[t ]\d{2}:\d{2}:\d{2}(?:\.\d+)?z?', row_text)))[-20:],
                              'durationsOrCounts': re.findall(r'\b\d+\s+(?:restarts?|stops?|minutes?|hours?)\b', row_text)})
        emit('detectorTable', {'detector': detector_id, 'table': index,
                               'columns': columns, 'rowCount': len(rows),
                               'matchingRows': summaries[:40]})
