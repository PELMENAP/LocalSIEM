# LocalSIEM

Учебная SIEM-система для локальной демонстрации полного цикла: **событие → нормализация → сохранение → анализ → обнаружение угрозы → предупреждение → расследование инцидента → аналитика**.

Модульный монолит на ASP.NET Core (.NET 10) + PostgreSQL 16 + React/TypeScript. Работает на одном ПК без облачных сервисов.

## Состав

| Компонент | Путь | Назначение |
| --- | --- | --- |
| Domain | `src/LocalSIEM.Domain` | Сущности, перечисления, допустимые переходы статусов |
| Application | `src/LocalSIEM.Application` | Приём и нормализация событий, Detection Engine, алерты, инциденты, симулятор, дашборд, пользователи, источники, настройки |
| Infrastructure | `src/LocalSIEM.Infrastructure` | EF Core + PostgreSQL, конфигурации таблиц и индексов, сидинг, хеширование паролей |
| Api | `src/LocalSIEM.Api` | REST API, SignalR-хаб, фоновые воркеры (детекция, мониторинг источников), раздача собранного фронтенда |
| Windows Collector | `src/LocalSIEM.Collector.Windows` | Отдельный процесс: подписка на журнал Security (4624, 4625, 4672, 4720) и отправка в HTTP API |
| Web | `web` | React + TypeScript + Vite + Tailwind CSS + ECharts |
| Тесты | `tests` | Юнит-тесты Detection Engine и нормализации, интеграционные тесты API на PostgreSQL в Testcontainers |

## Требования

- .NET SDK 10.0
- Node.js 20+ (для сборки фронтенда)
- Docker Desktop (PostgreSQL; Testcontainers для интеграционных тестов)
- Windows — только для Windows Collector; остальная система кроссплатформенна

## Быстрый старт

```powershell
copy .env.example .env
# в .env задайте POSTGRES_PASSWORD и LOCALSIEM_ADMIN_PASSWORD
powershell -ExecutionPolicy Bypass -File scripts/dev.ps1
```

Linux/macOS: `cp .env.example .env`, заполнить, затем `./scripts/dev.sh`.

Скрипт:

1. поднимает PostgreSQL (`docker compose up -d --wait`);
2. генерирует `LOCALSIEM_JWT_KEY` в `.env`, если он пуст;
3. при первом запуске создаёт миграцию `InitialCreate` (`scripts/create-migration.*`);
4. при отсутствии `src/LocalSIEM.Api/wwwroot/index.html` собирает фронтенд;
5. запускает API на **http://localhost:5080** — он же отдаёт интерфейс.

При старте API применяет миграции и создаёт роли, администратора (`admin` / `LOCALSIEM_ADMIN_PASSWORD`), источник «Event Simulator» и 5 правил обнаружения. Если пароль администратора не задан, он генерируется и один раз выводится в лог.

### Ручной запуск

```bash
docker compose up -d --wait
dotnet user-secrets --project src/LocalSIEM.Api set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=localsiem;Username=localsiem;Password=<пароль>"
dotnet user-secrets --project src/LocalSIEM.Api set "Jwt:Key" "<случайная строка от 32 символов>"
dotnet user-secrets --project src/LocalSIEM.Api set "Seed:AdminPassword" "<пароль администратора>"
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/LocalSIEM.Infrastructure --startup-project src/LocalSIEM.Api --output-dir Persistence/Migrations
dotnet run --project src/LocalSIEM.Api
```

Фронтенд в режиме разработки (горячая перезагрузка, прокси `/api` и `/hubs` на :5080):

```bash
npm --prefix web install
npm --prefix web run dev      # http://localhost:5173
npm --prefix web run build    # сборка в src/LocalSIEM.Api/wwwroot
```

Если миграция не создана, API создаёт схему через `EnsureCreated` и пишет предупреждение в лог — система работает, но последующие изменения схемы потребуют пересоздания базы.

## Конфигурация

| Ключ | По умолчанию | Назначение |
| --- | --- | --- |
| `ConnectionStrings:Postgres` | — | Строка подключения (обязательна) |
| `Jwt:Key` | случайный при старте | Ключ подписи JWT, ≥ 32 байт |
| `Jwt:LifetimeMinutes` | 60 | Срок действия токена |
| `Seed:AdminUser` / `Seed:AdminPassword` | `admin` / сгенерированный | Первый администратор |
| `Detection:QueueCapacity` | 10000 | Ёмкость очереди событий на анализ |
| `RateLimits:LoginPerMinute` | 5 | Попыток входа в минуту с одного IP |
| `RateLimits:IngestEventsPerSecond` | 1000 | Событий в секунду на один источник |

Переменные окружения используют `__` вместо `:` (`ConnectionStrings__Postgres`). Секреты в репозитории не хранятся.

Системные настройки (размер связки событий алерта, порог простоя источника, предел скорости симулятора) меняются в интерфейсе: **Settings → Система**.

## Как устроена обработка

1. Источник отправляет `POST /api/ingest/events` с заголовком `X-Api-Key`. Симулятор вызывает тот же `IEventIngestionService` внутри процесса.
2. Пакет (до 500 событий) валидируется, нормализуется в `SecurityEvent` (исходник — в `raw_data jsonb`), сохраняется одной транзакцией; ответ — `202 Accepted`.
3. События попадают в ограниченный `Channel`; единственный `DetectionWorker` прогоняет их через активные правила.
4. Окна правил хранятся в памяти и вытесняются по времени события — история из БД на каждое событие не читается. При старте окна прогреваются событиями за максимальное окно, а открытые эпизоды восстанавливаются из `alerts`.
5. Срабатывание создаёт алерт со связанными событиями. Повторные совпадения той же группы в пределах cooldown дописываются в существующий алерт, а не создают дубликат.
6. Уведомления (`EventsIngested`, `AlertCreated`, `IncidentCreated`, `SourceStatusChanged` и др.) уходят по SignalR; полные данные клиент берёт через REST.

### Правила

| Код | Тип | Логика по умолчанию | Критичность |
| --- | --- | --- | --- |
| `BRUTE_FORCE` | Threshold | ≥ 5 LoginFailed с одного IP за 60 с | High |
| `PASSWORD_SPRAYING` | DistinctCount | ≥ 5 разных учёток в LoginFailed с одного IP за 120 с | Critical |
| `SUCCESS_AFTER_BRUTE_FORCE` | Sequence | ≥ 3 LoginFailed, затем LoginSuccess, тот же IP + учётка, 300 с | Critical |
| `PRIVILEGE_ESCALATION` | SingleEvent | RoleChanged с `newRole` ∈ {Administrator, Domain Admins, …} | High |
| `SUSPICIOUS_ACCESS` | Threshold + фильтр | ≥ 20 ResourceAccess с `protected = true` на пользователя за 60 с | Medium |

Все параметры (порог, окно, cooldown, группировка, фильтр, критичность, активность) редактируются в **Settings → Правила**; там же создаются новые правила любого из четырёх типов. Граница окна включительная: события на расстоянии ровно `window` секунд попадают в одно окно.

## Демонстрация (~15 минут)

| # | Сценарий симулятора | Ожидаемые алерты |
| --- | --- | --- |
| 1 | Нормальная активность | нет |
| 2 | Серия неудачных входов (4) | нет — порог не достигнут |
| 3 | Brute Force | Brute Force (High), один алерт, число событий растёт |
| 4 | Password Spraying | Password Spraying (Critical) + Brute Force (High) |
| 5 | Успешный вход после серии | Brute Force (High) + Successful Login After Brute Force (Critical) |
| 6 | Изменение привилегий | Privilege Escalation (High) |
| 7 | Подозрительный доступ | Suspicious Access (Medium) |

Порядок показа:

1. Войти администратором, открыть **Dashboard**.
2. **Event Simulator** → «Нормальная активность»: события идут в реальном времени, алертов нет.
3. «Серия неудачных входов»: алертов нет.
4. «Brute Force»: всплывает уведомление, алерт появляется на Dashboard без перезагрузки.
5. Повторить «Brute Force» в течение минуты: новый алерт не создаётся, растёт число событий у существующего.
6. «Password Spraying» и «Успешный вход после серии»: по два алерта.
7. **Event Explorer**: фильтр по IP атакующего, открыть событие, показать исходный JSON.
8. **Threat Detection**: причина срабатывания; отключить Brute Force, повторить сценарий — алерта нет; включить обратно.
9. **Alerts**: отметить алерты сценариев 4 и 5 → «Создать инцидент», назначить ответственного, комментарий, Open → Investigating → Resolved; показать временную шкалу и историю.
10. **Settings → Источники**: создать источник HTTP API, выполнить показанную команду `curl` (или `scripts/send-events.ps1 -ApiKey <ключ>`) — событие появляется в Event Explorer; запрос без ключа получает 401.
11. Создать аналитика и войти им: Settings недоступны, API отвечает 403.
12. **Event Simulator → Очистить демо-данные** с вводом `DELETE DEMO DATA`.

## Windows Event Collector

```powershell
# 1. В LocalSIEM: Settings → Источники → Добавить, тип «Windows Collector», скопировать ключ
dotnet user-secrets --project src/LocalSIEM.Collector.Windows set "Collector:ApiKey" "<ключ>"
dotnet run --project src/LocalSIEM.Collector.Windows      # остановка: Ctrl+C
```

- Сборщик подписывается на новые события журнала `Security` с ID 4624, 4625, 4672, 4720, пакетами отправляет их в `/api/ingest/events`, при недоступности API повторяет отправку с нарастающей паузой.
- **Права.** Чтение журнала Security требует членства в группе «Event Log Readers» («Читатели журнала событий») или запуска от имени администратора. Добавить пользователя (нужны права администратора, затем перелогиниться): `net localgroup "Event Log Readers" %USERNAME% /add`. Без прав сборщик пишет понятную ошибку и повторяет попытку каждые 30 с; остальная система от него не зависит.
- **Аудит.** Событие 4625 появляется, только если включён аудит неудачных входов: `auditpol /set /subcategory:"Logon" /success:enable /failure:enable` (от администратора).
- Проверка: `runas /user:%COMPUTERNAME%\%USERNAME% cmd` с неверным паролем → событие LoginFailed (4625) в Event Explorer.
- Как служба Windows: `dotnet publish src/LocalSIEM.Collector.Windows -c Release -o C:\LocalSIEM\collector`, ключ указать в `C:\LocalSIEM\collector\appsettings.json` (user-secrets в службе не читаются), затем `sc create LocalSIEMCollector binPath= "C:\LocalSIEM\collector\LocalSIEM.Collector.Windows.exe"` и `sc start LocalSIEMCollector`.
- Учётные записи компьютеров (`…$`) и системные (SYSTEM, LOCAL SERVICE, …) отфильтровываются — настраивается в `appsettings.json` сборщика.

Нормализатор на сервере переводит Windows-события в общий формат: 4624 → LoginSuccess, 4625 → LoginFailed, 4672 → PrivilegedLogon, 4720 → AccountCreated. Поэтому правила Brute Force и Password Spraying работают и на реальных неудачных входах.

## Тесты

```bash
dotnet test tests/LocalSIEM.Application.Tests        # без внешних зависимостей
dotnet test tests/LocalSIEM.Api.IntegrationTests     # нужен запущенный Docker
```

- **Application.Tests** — 65 проверок: каждое правило (порог не достигнут / достигнут / граница окна / событие за окном / разные IP / разные пользователи / регистр имён), подавление дублей и cooldown, закрытие эпизода, одновременное срабатывание нескольких правил, прогрев, валидация входящих событий, нормализация (включая Windows ID), переходы статусов.
- **Api.IntegrationTests** — вход и 401, разграничение ролей (403), приём через HTTP API (401 без ключа, 400 с ошибками по полям, 202 и поиск сохранённого события), Brute Force через HTTP с проверкой отсутствия дубля, отсутствие алерта ниже порога, полный цикл инцидента (409 на недопустимый переход и устаревшую версию, комментарии, история, временная шкала), прогон симулятора и очистка демо-данных.

## API

Основные группы: `/api/auth`, `/api/ingest/events`, `/api/events`, `/api/alerts`, `/api/incidents`, `/api/rules`, `/api/dashboard`, `/api/sources`, `/api/simulator`, `/api/users`, `/api/user-options`, `/api/roles`, `/api/settings`, `/api/audit`; хаб `/hubs/siem`. Ошибки возвращаются в формате ProblemDetails (400 с ошибками по полям, 401, 403, 404, 409, 429).

Пример события:

```json
{
  "events": [
    {
      "timestamp": "2026-10-31T12:00:05Z",
      "eventType": "LoginFailed",
      "severity": "Low",
      "sourceIp": "10.0.0.15",
      "username": "j.smith",
      "hostname": "WS-014",
      "message": "Invalid password",
      "attributes": { "protected": false },
      "raw": { "any": "original payload" }
    }
  ]
}
```

Источник определяется по ключу API, поле `source` в теле не принимается. Типы событий: LoginSuccess, LoginFailed, Logout, RoleChanged, AccountCreated, PrivilegedLogon, ResourceAccess, Generic и любые другие `[A-Za-z][A-Za-z0-9_.-]`.

## Безопасность

- Пароли — PBKDF2 (`PasswordHasher` ASP.NET Core Identity), JWT HS256, проверка ролей на сервере для каждого эндпоинта и хаба.
- Ключи источников хранятся как SHA-256 и показываются один раз.
- Rate limiting входа и приёма событий, ограничение размера пакета и полей.
- Журнал действий: входы (в т. ч. неудачные), изменения пользователей, правил, источников, настроек, статусов алертов и инцидентов, запуск симулятора, очистка.
- Только мониторинг: система ничего не блокирует и не меняет настройки Windows.
