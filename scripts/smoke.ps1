$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5179/api/miniapp'
$headers = @{'X-Demo-User-Id'='1'}
$login = Invoke-RestMethod -Uri "$base/admin/signin" -Method Post -Headers $headers -ContentType 'application/json' -Body '{"password":"demo-admin"}'
$headers['X-Admin-Token'] = $login.token
$start = @{
  chatTelegramId = -1001
  senderTelegramId = 77
  messageTelegramId = 10001
  telegramDateUtc = '2026-09-28T07:03:00+03:00'
  text = '🔴 07:02:57 ВФС: Концентрація залишкового хлору менше 0,3 ppm на протязі 1 години. (Зафіксовано концентрацію: 0.13 ppm)'
} | ConvertTo-Json
$created = Invoke-RestMethod -Uri "$base/demo/message" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $start
if ($created.status -ne 'Created') { throw "Create failed: $($created | ConvertTo-Json)" }
$id = $created.incidentId
$again = Invoke-RestMethod -Uri "$base/demo/message" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $start
if ($again.status -ne 'Duplicate') { throw 'Duplicate failed' }
$end = @{
  chatTelegramId = -1001
  senderTelegramId = 77
  messageTelegramId = 10002
  telegramDateUtc = '2026-09-28T09:08:00+03:00'
  text = '🟢 09:07:57 ВФС: Концентрація залишкового хлору менше 0,3 ppm на протязі 1 години. Тривалість: 2h 5m 0s'
} | ConvertTo-Json
$resolved = Invoke-RestMethod -Uri "$base/demo/message" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $end
if ($resolved.status -ne 'Resolved') { throw "Resolve failed: $($resolved | ConvertTo-Json)" }
$before = Invoke-RestMethod -Uri "$base/incidents/$id" -Headers $headers
if ($before.problemState -ne 'Resolved' -or $before.answerState -ne 'Unanswered') { throw 'Wrong state before response' }
$reply = @{incidentId=$id;telegramUserId=900002;templateId=1;text=''} | ConvertTo-Json
$saved = Invoke-RestMethod -Uri "$base/demo/response" -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $reply
$after = Invoke-RestMethod -Uri "$base/incidents/$id" -Headers $headers
if ($after.problemState -ne 'Resolved' -or $after.answerState -ne 'Answered' -or $after.endedAtUtc -ne $before.endedAtUtc) { throw 'Wrong state after response' }
if ($after.responses.Count -ne 1 -or $after.responses[0].author -ne 'Оператор ВФС') { throw 'Response attribution failed' }
$dashboard = Invoke-RestMethod -Uri "$base/dashboard" -Headers $headers
if ($dashboard.PSObject.Properties.Name -contains 'answeredAfterResolution') { throw 'Removed dashboard metric is still present' }
$dated = Invoke-RestMethod -Uri "$base/dashboard?period=range&fromDate=2026-09-28&toDate=2026-09-28" -Headers $headers
$outside = Invoke-RestMethod -Uri "$base/incidents?period=range&fromDate=2026-09-27&toDate=2026-09-27" -Headers $headers
if ($dated.total -ne 1 -or $outside) { throw 'Date filters failed' }
Write-Output "PASS incident=$id duplicate=$($again.status) before=$($before.answerState) after=$($after.answerState) duration=$(([datetimeoffset]$after.endedAtUtc - [datetimeoffset]$after.startedAtUtc).TotalSeconds)s dashboard_total=$($dashboard.total)"
