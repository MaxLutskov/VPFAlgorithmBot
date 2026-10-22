# VPF Algorithm Bot

Застосунок для обліку виробничих алгоритмів. Події надходять із Telegram-чатів; готові та власні відповіді можна надавати в групі, у приватному чаті з ботом або в картці Telegram Mini App. Адміністрування, дешборд та історія — у Mini App.

Документацію звірено з кодом `main` на коміті `becbf66` і доповнено локальною зміною операційних даних 22 жовтня 2026 року. [Поточна поведінка та карта коду](docs/current-behavior.md) описують правила для наступних змін; [план розробки](DEVELOPMENT_PLAN.md) збережений як початкове технічне завдання.

## Що реалізовано

- ASP.NET Core на .NET 10, EF Core з SQL Server/Azure SQL, окрема схема `vpfalgo`.
- Отримання подій через Telegram webhook або polling; demo-режим через тестовий API.
- Зіставлення червоного й зеленого повідомлень, захист від повторів, окремі стани проблеми й відповіді.
- Операційні дані в дужках із початкового червоного повідомлення зберігаються окремо для кожного випадку й не перезаписуються зеленим. Mini App показує їх у списку та картці; бот додає їх у повідомлення, нагадування та зведення.
- Приватні сповіщення відповідальним за об’єктом, кнопки шаблонів у групі та приватному чаті, команда `/answer НОМЕР текст` і форма відповіді в Mini App; відповідь доступна після завершення.
- Власна відповідь у групі прив’язана до працівника та запиту бота через Telegram reply; підтвердження групової відповіді надсилається під початковим повідомленням алгоритму. Звичайна розмова не записується як відповідь.
- Груповий запит бота — коротке «Надайте відповідь:» з кнопками, надіслане reply на початкове повідомлення алгоритму. Назва й операційні дані не дублюються в тексті цього запиту.
- Запити доступу `/start`, підтвердження, службові імена, ролі та доступи до об’єктів.
- Mini App: дешборд, історія, без відповіді, картка, часові фільтри «сьогодні», «вчора», «за датами» й «за весь час», налаштування чатів, правил, маршрутів, шаблонів і користувачів, журнал адміністративних змін. Період застосовується за датою початку алгоритму в часі Києва.
- Окремі шаблони для кожного алгоритму та їхні версії, outbox з повторними спробами, нагадування до появи відповіді.
- Щоденні зведення активних алгоритмів без відповіді: чат-одержувач, час Києва й об’єкти задаються в Mini App; джерело події може бути іншим чатом.
- Автоматичне створення довідника з повідомлень, спільні типи за сімейством об’єктів, каталог із 69 рядків `water-algorithms.json`, імпорт Telegram Desktop за останні 72 години без сповіщень про минулі події.
- Поточний початковий перелік: ВФС, НС, ПНС-1–3, РЧВ 1–14, 22, 42, 43, 47, 49 та ІПС. Групове призначення РЧВ/ПНС доступне в адмініструванні; це вибір наявних об’єктів, а не автоматичний доступ до майбутніх.
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
| `Admin:Password` | Пароль зі змінної середовища Azure Web App `Admin__Password` |
| `Admin:KeyPath` | Каталог ключів адміністративних сесій; в Azure типово використовується `/home/data-protection-keys` |
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

Підхід такий самий, як у локального StockPartsBot: окремий Linux App Service з .NET 10, за потреби той самий App Service Plan, спільна Azure SQL Database **лише через окрему схему `vpfalgo`**, GitHub Actions з OIDC. Шаблон `infra/main.bicep` не створює SQL Server і не змінює StockPartsBot. До першого запуску створіть окремого SQL-користувача або managed identity з правами на схему `vpfalgo`; перевірте міграції на тестовій копії. Пароль адміністратора задайте безпосередньо у змінній середовища `Admin__Password` в Azure Web App.

Під час створення App Service у Bicep обов’язково передайте Telegram ID першого адміністратора через `adminBootstrapTelegramId`; цей користувач активує доступ командою `/start`. Без цього початкове підтвердження нових користувачів буде недоступне.

Workflow збирає Mini App і .NET, виконує domain-тести, публікує лише вебпроєкт у Web App `VPFAlgorithmBot` через GitHub OIDC, налаштований Azure Deployment Center. Деплой запускається при push у `main` або вручну та перевіряє `/health` на фактичному домені застосунку. Для запуску необхідні Azure SQL, налаштування Telegram і пароль адміністратора у змінній середовища за [інструкцією](docs/azure-deploy.md).

## Дані та звітність

Дані зберігаються в схемі `vpfalgo`. Міграції EF Core використовують `vpfalgo.__EFMigrationsHistory`. [Словник Power BI](docs/power-bi.md) описує `vpfalgo.PowerBiIncidents` і `vpfalgo.PowerBiResponses`. У demo ці SQL views недоступні, оскільки EF InMemory не виконує SQL Server migration.

## Межі перевірок та поточні обмеження

- Під час локальної перевірки 22 жовтня 2026 року реальне отримання повідомлень Bot_Outlook, Azure SQL і App Service не перевірялися. Це не висновок про стан розгорнутого застосунку.
- Demo InMemory не зберігає дані між перезапусками. Для перевірки міграцій і збереження потрібен тестовий SQL Server або Azure SQL.
- Налаштування графіків змін і автоматичне заміщення працівників поки не реалізовані.
- Розбір повідомлень базується на правилах regex; для нових форматів додайте й перевірте правила в Mini App.
- Під час міграції старі шаблони автоматично прив’язуються до алгоритму, якщо він визначається однозначно за об’єктом і категорією. Решта зберігається вимкненою; адміністратор може призначити їм алгоритм і ввімкнути знову.

Поточний стан перевірок і попередження NuGet наведено в [звіті](docs/TEST_REPORT.md). Зведення та правила доставки описані в [поточній поведінці](docs/current-behavior.md#щоденні-зведення).
