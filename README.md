# VPF Algorithm Bot

Тестовий застосунок для обліку виробничих алгоритмів. Повідомлення про проблеми й кнопки готових відповідей працюють у Telegram-чаті з ботом. Адміністрування, дешборд та історія — у Telegram Mini App.

## Що реалізовано

- ASP.NET Core на .NET 10, EF Core з SQL Server/Azure SQL, окрема схема `vpfalgo`.
- Отримання подій через Telegram webhook або polling; demo-режим через тестовий API.
- Зіставлення червоного й зеленого повідомлень, захист від повторів, окремі стани проблеми й відповіді.
- Приватні сповіщення відповідальним, кнопки шаблонів і команда `/answer НОМЕР текст`; відповідь доступна після завершення.
- Запити доступу `/start`, підтвердження, службові імена, ролі та доступи до об’єктів.
- Mini App: дешборд, історія, без відповіді, картка, часові фільтри «сьогодні», «вчора», «за датами» й «за весь час», налаштування чатів, правил, маршрутів, шаблонів і користувачів, журнал адміністративних змін. Період застосовується за датою початку алгоритму в часі Києва.
- Окремі шаблони для кожного алгоритму та їхні версії, outbox з повторними спробами, нагадування до появи відповіді.
- EF Core migration і SQL views для Power BI.

## Локальний запуск без Azure

Потрібні .NET 10 SDK, Node.js 24 і pnpm 11. Проєкт за замовчуванням запускається в `Demo`. Для demo використовується EF Core InMemory: він дозволяє перевіряти бізнес-логіку без Azure SQL, але дані demo зникають після перезапуску. Режим Azure SQL використовує постійні таблиці.

```powershell
cd src/VPFAlgorithmBot/ClientApp
pnpm install --frozen-lockfile
pnpm build
cd ../../..
dotnet restore VPFAlgorithmBot.slnx --configfile NuGet.Config
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/VPFAlgorithmBot/VPFAlgorithmBot.csproj --urls http://127.0.0.1:5179
```

Відкрити `http://127.0.0.1:5179/miniapp/?demoUser=1`. Тестовий адміністратор — користувач 1, пароль адміністративної панелі в Development/demo — `demo-admin`. Тестовий оператор — `?demoUser=2`. Demo-ідентифікація вимкнена поза Development.

Тестова група має chat ID `-1001`, відправник `77`. Кнопки Telegram фізично відправляються лише в режимах `Polling`/`Webhook`; у demo відповіді та події можна подати через API. Готовий наскрізний тест:

```powershell
pwsh -NoProfile -File scripts/smoke.ps1
pwsh -NoProfile -File scripts/admin-smoke.ps1
```

Domain-тести:

```powershell
dotnet run --project tests/VPFAlgorithmBot.Tests/VPFAlgorithmBot.Tests.csproj
```

## Налаштування реального режиму

Параметри надаються через .NET User Secrets локально або захищені налаштування Azure App Service:

| Ключ | Призначення |
|---|---|
| `Telegram:Mode` | `Polling` локально або `Webhook` в Azure |
| `Telegram:BotToken` | Токен нового бота |
| `Telegram:WebhookSecret` | Окремий випадковий секрет URL |
| `ConnectionStrings:BotDatabase` | Azure SQL connection string |
| `Admin:BootstrapTelegramId` | Telegram ID першого адміністратора |
| `Admin:Password` | Пароль із Azure Key Vault reference |
| `Admin:KeyPath` | Каталог ключів адміністративних сесій, на Azure `/home/data-protection-keys` |
| `Reminder:Minutes` | Типовий інтервал, 0 вимикає; адміністратор може змінити в Mini App |

Приклад локальних User Secrets:

```powershell
dotnet user-secrets init --project src/VPFAlgorithmBot
dotnet user-secrets set 'Telegram:Mode' 'Polling' --project src/VPFAlgorithmBot
dotnet user-secrets set 'Telegram:BotToken' 'TOKEN' --project src/VPFAlgorithmBot
dotnet user-secrets set 'ConnectionStrings:BotDatabase' 'Server=...;Database=...;Encrypt=True;...' --project src/VPFAlgorithmBot
dotnet user-secrets set 'Admin:BootstrapTelegramId' '123456789' --project src/VPFAlgorithmBot
dotnet user-secrets set 'Admin:Password' 'STRONG_PASSWORD' --project src/VPFAlgorithmBot
```

Режим `Polling` видаляє попередній webhook цього бота. На Azure використовуйте лише `Webhook` і реєструйте адресу `https://APP.azurewebsites.net/api/telegram/WEBHOOK_SECRET`. Щоб отримувати повідомлення іншого бота в групі, перевірте Bot-to-Bot Communication, права бота та Privacy Mode в конкретному чаті; це потребує реального тесту.

## Azure

Покрокове налаштування нового репозиторію, Azure-ресурсів, GitHub OIDC і першого деплою: [інструкція з деплою](docs/azure-deploy.md).

Підхід такий самий, як у локального StockPartsBot: окремий Linux App Service з .NET 10, за потреби той самий App Service Plan, спільна Azure SQL Database **лише через окрему схему `vpfalgo`**, GitHub Actions з OIDC. Шаблон `infra/main.bicep` не створює SQL Server і не змінює StockPartsBot. До першого запуску створіть окремого SQL-користувача або managed identity з правами на схему `vpfalgo`; перевірте міграції на тестовій копії. App Service managed identity має отримати право читання потрібного секрету Key Vault.

Під час створення App Service у Bicep обов’язково передайте Telegram ID першого адміністратора через `adminBootstrapTelegramId`; цей користувач активує доступ командою `/start`. Без цього початкове підтвердження нових користувачів буде недоступне.

Workflow збирає Mini App і .NET, виконує domain-тести, публікує артефакт. Деплой запускається вручну (`workflow_dispatch`) у GitHub environment `test` з налаштованими `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_WEBAPP_NAME`. Після деплою потрібні `/health`, реальна перевірка Telegram і SQL views. Наявність шаблону деплою не означає, що Azure-ресурси вже налаштовані.

## Дані та звітність

Дані зберігаються в схемі `vpfalgo`. Міграції EF Core використовують `vpfalgo.__EFMigrationsHistory`. [Словник Power BI](docs/power-bi.md) описує `vpfalgo.PowerBiIncidents` і `vpfalgo.PowerBiResponses`. У demo ці SQL views недоступні, оскільки EF InMemory не виконує SQL Server migration.

## Відомі межі тестового випуску

- Реальне отримання повідомлень Bot_Outlook, Azure SQL, Key Vault і App Service не перевірені без відповідних доступів.
- Demo InMemory не зберігає дані між перезапусками. Для перевірки міграцій і збереження потрібен тестовий SQL Server або Azure SQL.
- Налаштування графіків змін і автоматичне заміщення працівників поки не реалізовані.
- Розбір повідомлень базується на правилах regex; для нових форматів додайте й перевірте правила в Mini App.
- Під час міграції старі шаблони автоматично прив’язуються до алгоритму, якщо він визначається однозначно за об’єктом і категорією. Решта зберігається вимкненою; адміністратор може призначити їм алгоритм і ввімкнути знову.

Поточний стан перевірок наведено в [звіті](docs/TEST_REPORT.md).
