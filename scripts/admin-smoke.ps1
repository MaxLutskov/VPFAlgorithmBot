param([string]$BaseUrl = "http://127.0.0.1:5179")
$ErrorActionPreference = 'Stop'
$base = "$BaseUrl/api/miniapp"
$headers = @{'X-Demo-User-Id'='1'}

try {
  Invoke-RestMethod -Uri "$base/admin/catalog" -Headers $headers | Out-Null
  throw 'Admin catalog opened without a password session'
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
  if ($_.Exception.Response.StatusCode.value__ -ne 403) { throw }
}
try {
  Invoke-RestMethod -Uri "$base/admin/signin" -Method Post -Headers @{'X-Demo-User-Id'='2'} -ContentType 'application/json' -Body '{"password":"demo-admin"}' | Out-Null
  throw 'Operator received an administrator session'
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
  if ($_.Exception.Response.StatusCode.value__ -ne 403) { throw }
}

$login = Invoke-RestMethod -Uri "$base/admin/signin" -Method Post -Headers $headers -ContentType 'application/json' -Body '{"password":"demo-admin"}'
$headers['X-Admin-Token'] = $login.token

function Send-Json($path, $method, $body) {
  Invoke-RestMethod -Uri "$base$path" -Method $method -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ($body | ConvertTo-Json -Depth 10)
}
function Catalog { Invoke-RestMethod -Uri "$base/admin/catalog" -Headers $headers }

Send-Json '/admin/chats' 'POST' @{telegramChatId=-1099;senderTelegramId=99;name='Тестовий чат';enabled=$true} | Out-Null
Send-Json '/admin/objects' 'POST' @{code='ТЕСТ';name='Тестовий обект';enabled=$true} | Out-Null
Send-Json '/admin/categories' 'POST' @{name='Тестова категорія';enabled=$true} | Out-Null
$catalog = Catalog
$chat = $catalog.chats | Where-Object telegramChatId -eq -1099
$object = $catalog.objects | Where-Object code -eq 'ТЕСТ'
$category = $catalog.categories | Where-Object name -eq 'Тестова категорія'
if (!$chat -or !$object -or !$category) { throw 'Catalog creation failed' }

Send-Json "/admin/chats/$($chat.id)" 'PUT' @{telegramChatId=-1099;senderTelegramId=99;name='Тестовий чат оновлено';enabled=$true} | Out-Null
Send-Json '/admin/algorithms' 'POST' @{objectId=$object.id;categoryId=$category.id;name='Тестовий алгоритм';matchPattern='ТЕСТ';priority=10;enabled=$true} | Out-Null
Send-Json '/admin/routes' 'POST' @{chatId=$chat.id;objectId=$object.id;categoryId=$category.id;userId=2;priority=10;enabled=$true} | Out-Null
$algorithm = (Catalog).algorithms | Where-Object name -eq 'Тестовий алгоритм'
if (!$algorithm) { throw 'Algorithm creation failed' }
try {
  Send-Json '/admin/templates' 'POST' @{title='Без алгоритму';text='Недопустимо';sortOrder=10;enabled=$true} | Out-Null
  throw 'Template without an algorithm was accepted'
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
  if ($_.Exception.Response.StatusCode.value__ -ne 400) { throw }
}
Send-Json '/admin/templates' 'POST' @{algorithmRuleId=$algorithm.id;title='Тестова відповідь';text='Перевірено показники';sortOrder=10;enabled=$true} | Out-Null
Send-Json '/admin/scopes' 'POST' @{userId=2;objectId=$object.id} | Out-Null
$catalog = Catalog
$template = $catalog.templates | Where-Object title -eq 'Тестова відповідь'
if (!$template -or $template.version -ne 1 -or !($catalog.scopes | Where-Object { $_.userId -eq 2 -and $_.objectId -eq $object.id })) { throw 'Template or scope creation failed' }
if ($template.algorithmRuleId -ne $algorithm.id) { throw 'Template is not bound to its algorithm' }
try {
  Send-Json "/admin/templates/$($template.id)" 'PUT' @{algorithmRuleId=1;title='Тестова відповідь';text='Неправильний алгоритм';sortOrder=10;enabled=$true} | Out-Null
  throw 'Template was reassigned to another algorithm'
} catch [Microsoft.PowerShell.Commands.HttpResponseException] {
  if ($_.Exception.Response.StatusCode.value__ -ne 400) { throw }
}
Send-Json "/admin/templates/$($template.id)" 'PUT' @{algorithmRuleId=$algorithm.id;title='Тестова відповідь';text='Показники нормалізовані';sortOrder=10;enabled=$true} | Out-Null
Send-Json '/admin/reminder' 'PUT' @{minutes=15} | Out-Null
$preview = Send-Json '/admin/preview' 'POST' @{text='🔴 12:00:00 ТЕСТ: демонстрація';telegramDateUtc='2026-09-28T12:00:10+03:00'}
if ($preview.name -ne 'Тестовий алгоритм') { throw 'Preview classification failed' }
$catalog = Catalog
if (($catalog.templates | Where-Object id -eq $template.id).version -ne 2) { throw 'Template version did not advance' }
if (($catalog.templateVersions | Where-Object templateId -eq $template.id).Count -ne 2) { throw 'Template version history is incomplete' }
if (($catalog.settings | Where-Object key -eq 'reminder_minutes').value -ne '15') { throw 'Reminder setting not saved' }
if ($catalog.audit.Count -lt 8) { throw "Admin audit is incomplete: $($catalog.audit.Count)" }
Invoke-RestMethod -Uri "$base/admin/scopes/2/$($object.id)" -Method Delete -Headers $headers | Out-Null
if ((Catalog).scopes | Where-Object { $_.userId -eq 2 -and $_.objectId -eq $object.id }) { throw 'Scope delete failed' }
Write-Output "PASS chats objects categories algorithms routes templates scopes reminders preview audit=$($catalog.audit.Count)"

function Expect-BadRequest($path, $body) {
  try {
    Send-Json $path 'POST' $body | Out-Null
    throw "Invalid data accepted at $path"
  } catch [Microsoft.PowerShell.Commands.HttpResponseException] {
    if ($_.Exception.Response.StatusCode.value__ -ne 400) { throw }
  }
}
Expect-BadRequest '/admin/chats' @{telegramChatId=-1099;name='Duplicate'}
Expect-BadRequest '/admin/chats' @{telegramChatId=0;name='Zero'}
Expect-BadRequest '/admin/objects' @{code='ТЕСТ';name='Duplicate'}
Expect-BadRequest '/admin/objects' @{code='';name='Empty code'}
Expect-BadRequest '/admin/categories' @{name='Тестова категорія'}
Expect-BadRequest '/admin/algorithms' @{objectId=999999;categoryId=$category.id;name='Missing object';matchPattern='x'}
Expect-BadRequest '/admin/routes' @{userId=999999}
Expect-BadRequest '/admin/scopes' @{userId=2;objectId=999999}
if (((Catalog).templates | Where-Object algorithmRuleId -eq $algorithm.id).Count -ne 5) { throw 'New manual algorithm did not receive four initial templates' }
Write-Output 'PASS duplicate and invalid catalog data rejected; manual algorithms receive four templates'
