# Звіт тестування тестового випуску

Дата: 28 вересня 2026 року.

| Перевірка | Статус | Доказ або причина |
|---|---|---|
| Збірка .NET 10 | PASS | `dotnet build VPFAlgorithmBot.slnx --no-restore` |
| Збірка React Mini App | PASS | TypeScript і Vite зібрали `wwwroot/miniapp` |
| 🔴 створює випадок | PASS | Domain-тест і demo HTTP-сценарій |
| Повтор повідомлення | PASS | `Duplicate`, новий випадок не створено |
| 🟢 завершує випадок | PASS | Demo HTTP-сценарій |
| Завершений без відповіді | PASS | Стан `Resolved` + `Unanswered` |
| Відповідь після завершення | PASS | Стан `Resolved` + `Answered`, `EndedAtUtc` не змінився |
| Тривалість контрольного прикладу | PASS | 7 500 секунд = 2 год 5 хв |
| Автор відповіді | PASS | `Оператор ВФС` у картці |
| Подвійний callback | PASS | Domain-тест ідемпотентності `ActionKey` |
| Неавторизована відповідь | PASS | Domain-тест відхилення |
| Невідомий чат | PASS | Domain-тест відхилення |
| Часові фільтри | PASS | Domain-тести «сьогодні», «вчора», діапазону та помилкових дат; demo HTTP і Mini App |
| Шаблони за алгоритмом | PASS | Інший алгоритм не бачить і не може застосувати шаблон; CRUD прив’язує шаблон до вибраного алгоритму |
| Прибраний показник | PASS | Dashboard API не повертає `answeredAfterResolution`, Mini App і згенероване Power BI view не містять його |
| Адмінпанель без пароля | PASS | HTTP 403 без адміністративної сесії |
| CRUD налаштувань | PASS | Demo HTTP: чати, об’єкти, категорії, алгоритми, маршрути, шаблони й доступи |
| Версії шаблонів та аудит | PASS | Після редагування версія 2, історія має обидві версії; журнал містить щонайменше 12 записів на контрольному кроці |
| Правило класифікації та нагадування | PASS | Preview повернув тестовий алгоритм; інтервал 15 хв збережено |
| Operator Mini App | PASS | Браузер `?demoUser=2`: службове ім’я, статистика без адмінпанелі |
| Генерація SQL migration | PASS | `dotnet ef migrations script --idempotent`: створює схему `vpfalgo` та два Power BI views; SQL на сервері не виконувався |
| SQL Server migration і SQL views | BLOCKED | Скрипт згенеровано, але backfill старих шаблонів і SQL views не виконано на тестовому SQL Server/Azure SQL; у sandbox LocalDB не створює екземпляр |
| Bot_Outlook у реальному чаті | BLOCKED | Потрібні токен нового бота та тестовий Telegram-чат |
| Azure Key Vault | BLOCKED | Немає тестового Key Vault і managed identity |
| Azure App Service | BLOCKED | Немає тестового App Service та OIDC-конфігурації |
| Візуальна перевірка Mini App | PASS | У браузері перевірено дешборд, історію, картку випадку, адмінпанель та операторський режим; мобільний viewport ще не тестувався |
| Повна регресія CRUD адмінпанелі | PASS | `scripts/admin-smoke.ps1` у demo-режимі |

Команди: `dotnet run --project tests/VPFAlgorithmBot.Tests/VPFAlgorithmBot.Tests.csproj`, `pwsh -NoProfile -File scripts/smoke.ps1` та `pwsh -NoProfile -File scripts/admin-smoke.ps1` при запущеному demo-сервері. Позначки `BLOCKED` не означають успішної перевірки зовнішньої інтеграції.
