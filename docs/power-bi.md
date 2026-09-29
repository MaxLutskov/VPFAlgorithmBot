# Дані для Power BI

Джерело — Azure SQL Database, схема `vpfalgo`. Надайте окремому обліковому запису Power BI `SELECT` тільки на представлення цієї схеми. Доступ до таблиць користувачів і сирих повідомлень для звітів не потрібен.

## PowerBiIncidents

Один рядок на спрацювання. Поля `Id`, `ChatId`, `ChatName`, `ObjectId`, `ObjectCode`, `ObjectName`, `CategoryId`, `CategoryName`, `AlgorithmRuleId`, `AlgorithmName`, `StartedAtUtc`, `EndedAtUtc`, `Quality`, `ProblemState`, `AnswerState`, `FirstResponseAtUtc`, `ResponseCount`, `DurationSeconds`, `ResponseSeconds`. Назви об’єкта, категорії, алгоритму та чату доступні прямо у view, без прав читання базових таблиць.

- `ProblemState`: `Active` до зеленого повідомлення, потім `Resolved`.
- `AnswerState`: `Unanswered` до першої відповіді, потім `Answered`, навіть якщо відповідь надана після завершення.
- `DurationSeconds`: `EndedAtUtc - StartedAtUtc`, `NULL` для активного випадку.
- `ResponseSeconds`: `FirstResponseAtUtc - StartedAtUtc`, `NULL` до першої відповіді. Може бути більшим за тривалість.

## PowerBiResponses

Один рядок на відповідь: `Id`, `IncidentId`, `CreatedAtUtc`, `Text`, `TemplateId`, `TemplateVersion`, `EmployeeName`. Для кількості проблем рахуйте `PowerBiIncidents.Id`, а не рядки після приєднання всіх відповідей.

## Приклади SQL

```sql
SELECT CAST(StartedAtUtc AS date) AS UtcDate, COUNT(*) AS ProblemCount
FROM vpfalgo.PowerBiIncidents
GROUP BY CAST(StartedAtUtc AS date);

SELECT ObjectCode, ObjectName, CategoryName,
       COUNT(*) AS ProblemCount,
       AVG(CAST(DurationSeconds AS float)) AS AvgDurationSeconds,
       AVG(CAST(ResponseSeconds AS float)) AS AvgResponseSeconds
FROM vpfalgo.PowerBiIncidents
GROUP BY ObjectCode, ObjectName, CategoryName;

SELECT COUNT(*) AS ResolvedUnansweredNow
FROM vpfalgo.PowerBiIncidents
WHERE ProblemState = 'Resolved' AND AnswerState = 'Unanswered';
```

Поля часу зберігаються в UTC. У Power BI побудуйте локальну дату з Europe/Kyiv перед групуванням за виробничим днем; групування за UTC-добою може відрізнятися біля опівночі. Поточна кількість випадків без відповіді зміниться, коли оператор відповість пізніше. Для історичних «зрізів на дату» використовуйте часові мітки повідомлень і відповідей.
