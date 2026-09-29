# Тестовий деплой VPF Algorithm Bot в Azure через GitHub Actions

Ця інструкція відповідає наявному [workflow](../.github/workflows/main_vpfalgorithmbot.yml): кожен push у `main` збирає Mini App і .NET, запускає тести, публікує **лише вебпроєкт** та перевіряє `/health` на фактичному домені Web App. Workflow можна також запустити вручну через **Actions → Build and deploy VPF Algorithm Bot → Run workflow**. Він **не створює** App Service, Azure SQL або їхні налаштування.

## 1. Створити порожній репозиторій GitHub

Створіть приватний репозиторій без автоматичного README, `.gitignore` і ліцензії. У корені локального проєкту:

```powershell
git add .github .config .gitignore NuGet.Config README.md VPFAlgorithmBot.slnx azure.yaml docs infra scripts src tests
git commit -m "Initial VPF Algorithm Bot"
git branch -M main
git remote add origin https://github.com/OWNER/REPO.git
git push -u origin main
```

Замініть `OWNER/REPO`. Вибіркове `git add` не додає до репозиторію робочі Word-документи з `output/`. Перед push перегляньте `git status` і `git diff --cached --stat`. Не зберігайте токен Telegram, пароль адміністратора чи рядок підключення до SQL у Git.

## 2. Підготувати ресурси Azure

1. Створіть окремий **Linux App Service** для цього бота з runtime **.NET 10** і планом, що підтримує **Always On**. За згодою відповідальних можна використати наявний App Service Plan StockPartsBot після перевірки його місткості. Застосунок має бути доступний через HTTPS.
2. Підготуйте тестову Azure SQL Database або погоджене підключення до наявної бази. Застосунок створює таблиці та views у схемі `vpfalgo` і автоматично виконує EF Core migrations під час старту. Обліковий запис SQL повинен мати права на створення/зміну об'єктів у цій схемі. Спершу перевірте міграції на тестовій базі. Налаштуйте мережевий доступ App Service до SQL.
3. Створіть надійний пароль адміністратора і задайте його безпосередньо у змінній середовища App Service `Admin__Password`. Якщо там уже є Key Vault reference, замініть усе значення на сам пароль. Не записуйте пароль у GitHub, параметри workflow чи файли репозиторію.
4. У **App Service → Environment variables / Configuration → App settings** задайте:

   | Ключ | Значення |
   | --- | --- |
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `Telegram__Mode` | `Webhook` |
   | `Telegram__BotToken` | токен нового Telegram-бота |
   | `Telegram__WebhookSecret` | довгий випадковий URL-safe секрет |
   | `ConnectionStrings__BotDatabase` | робочий рядок підключення до Azure SQL; не залишайте `{your_username}` чи `Authentication=ActiveDirectoryIntegrated` із шаблону порталу |
   | `Admin__Password` | пароль адміністратора, заданий безпосередньо як значення змінної середовища |
   | `Admin__BootstrapTelegramId` | числовий Telegram ID першого адміністратора |
   | `Admin__KeyPath` | `/home/data-protection-keys` |

   Якщо плануєте повторні запуски або масштабування, налаштуйте постійне сховище для `/home`/ключів захисту даних. Збережіть налаштування App Service та дочекайтеся його перезапуску. Увімкніть HTTPS Only, Always On і Health check path `/health`.

   Для SQL-автентифікації приклад структури значення: `Server=tcp:YOUR-SERVER.database.windows.net,1433;Database=YOUR-DATABASE;User ID=YOUR-SQL-USER;Password=YOUR-SQL-PASSWORD;Encrypt=True;TrustServerCertificate=False;`. Замініть усі плейсхолдери реальними значеннями лише в Azure App Service. SQL-пароль і пароль адміністратора бота — різні секрети. SQL-користувачу потрібні права на міграції у схемі `vpfalgo`.

Замість ручного створення App Service можна використати [Bicep-шаблон](../infra/main.bicep). Він очікує **наявний** App Service Plan і не створює SQL та GitHub OIDC identity. Секрети при запуску Bicep передавайте як захищені параметри, не через файл у репозиторії.

## 3. Дати GitHub Actions право публікувати застосунок

Поточний Web App `VPFAlgorithmBot` вже отримав GitHub OIDC credential через Azure Deployment Center. Azure створив три GitHub secrets з префіксами `AZUREAPPSERVICE_CLIENTID_`, `AZUREAPPSERVICE_TENANTID_`, `AZUREAPPSERVICE_SUBSCRIPTIONID_`. Workflow використовує саме їх, тому не створюйте другий набір credentials чи другий workflow.

Якщо Azure-ресурси/репозиторій будуть створені заново, налаштуйте GitHub OIDC і роль **Website Contributor** на конкретний Web App. Для нових репозиторіїв перевіряйте immutable subject GitHub з owner/repository ID; поточний credential прив'язаний до `main`. Publish profile та довготривалий Azure client secret не потрібні.

## 4. Запустити перший деплой

1. Після push перевірте у **Actions**, що обидва jobs `build` і `deploy` успішні. Після публікації workflow перевіряє `/health` і позначає запуск помилкою, якщо застосунок не стартував.
2. Перевірте `https://vpfalgorithmbot-c6dshneadtdgenhw.westeurope-01.azurewebsites.net/health` і `/miniapp/` на тому самому домені. У нових App Service фактичний hostname може містити додатковий ідентифікатор та регіон: не конструюйте адресу лише з імені ресурсу.
3. Якщо `/health` не відповідає, перевірте **App Service → Log stream** та всі App settings із кроку 2. Зокрема, залишений за замовчуванням `Telegram:Mode=Demo` у Production призводить до помилки запуску; режим `Webhook` потребує Azure SQL, міграції якого виконуються ще до початку прийому запитів.

Для Web App `VPFAlgorithmBot` команда запуску явно задана як `dotnet VPFAlgorithmBot.dll`. Це усуває неоднозначність, якщо після старого деплою в `/home/site/wwwroot` залишився файл `VPFAlgorithmBot.Tests.runtimeconfig.json`: Oryx інакше запускає стандартний сайт Azure.

## 5. Підключити Telegram і перевірити сценарій

1. Створіть нового бота в BotFather. Додайте його в тестовий чат із Bot_Outlook. Для читання повідомлень іншого бота перевірте **Bot-to-Bot Communication Mode** і права/Privacy Mode у групі. Визначте числові `chat ID` та `sender ID` Bot_Outlook для правил джерела.
2. Після успішного деплою зареєструйте webhook Telegram за адресою `https://APP.azurewebsites.net/api/telegram/WEBHOOK_SECRET`, де `WEBHOOK_SECRET` точно збігається з `Telegram__WebhookSecret`. У поточній реалізації перевіряється секрет **у шляху URL**; сам лише параметр Telegram `secret_token` його не заміняє. Дозвольте update-и `message` і `callback_query`.
3. У BotFather задайте menu button / Mini App URL `https://APP.azurewebsites.net/miniapp/`. Перший адміністратор з ID у `Admin__BootstrapTelegramId` надсилає боту `/start`, після чого відкриває Mini App і входить у панель адміністратора з паролем зі змінної `Admin__Password`.
4. У Mini App додайте чат-джерело, ID відправника, об'єкти, алгоритми, маршрути відповідальних і **шаблони конкретних алгоритмів**. Створіть тестове червоне повідомлення та відповідне зелене, перевірте приватне сповіщення, відповідь після завершення, історію й SQL views для Power BI.

Не реєструйте webhook до перевірки доступності застосунку: Telegram повторюватиме доставку, якщо endpoint не відповідає. Для діагностики використовуйте Bot API `getWebhookInfo` та журнали App Service.

## Якщо перший запуск не вдався

| Симптом | Що перевірити |
| --- | --- |
| `azure/login` відхилено | Federated credential `subject` (зокрема immutable IDs), audience, tenant/client/subscription ID, роль на App Service. |
| Build не проходить | Логи `pnpm`, .NET restore, domain-тестів; ресурс Azure до цього етапу не потрібен. |
| Деплой пройшов, `/health` недоступний | Доступ до Azure SQL, реальний `ConnectionStrings__BotDatabase` без плейсхолдерів, права на міграції, App Service logs. `Failed to authenticate the user {your_username}` означає, що в App Service залишився шаблонний рядок підключення. |
| Не працює адмін-вхід | Значення `Admin__Password` в App Service, `Admin__BootstrapTelegramId`, `/start`. |
| Telegram не приносить повідомлень | `getWebhookInfo`, URL та секрет шляху, Bot-to-Bot Mode, права бота в групі, `chat ID` і `sender ID`. |

## Офіційні довідки

- [Azure App Service deployment through GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions)
- [GitHub OIDC subject claims, including immutable IDs](https://docs.github.com/en/actions/reference/security/oidc)
- [GitHub Actions environments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/control-deployments)
- [Telegram Bot-to-Bot Communication](https://core.telegram.org/bots/features)
- [Telegram Bot API: setWebhook and getWebhookInfo](https://core.telegram.org/bots/api)
