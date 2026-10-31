# Отчёт о проверке

Проект собирался в среде без доступа к NuGet (api.nuget.org заблокирован). Ниже — что именно проверено и чем, и что остаётся проверить на машине разработчика.

## Выполнено

| Что | Как | Результат |
| --- | --- | --- |
| Detection Engine, нормализация, валидация, переходы статусов | Исходники Domain, ядра детекции, нормализаторов и все файлы `tests/LocalSIEM.Application.Tests` скомпилированы .NET 10 SDK и прогнаны раннером, совместимым с атрибутами xUnit | 65 из 65 проверок пройдены |
| Компиляция всего бэкенда (Domain, Application, Infrastructure, Api) и интеграционных тестов | Компиляция .NET 10 SDK против заглушек API пакетов EF Core, Npgsql, JwtBearer, Serilog, xUnit, Testcontainers | 0 ошибок, 0 предупреждений |
| Windows Collector | Компиляция .NET 10 SDK со сборками общего фреймворка ASP.NET Core (System.Diagnostics.EventLog, Hosting) | 0 ошибок |
| Сырые SQL-запросы (дозапись связей алерта с `ON CONFLICT` и `LIMIT`, `date_bin` для динамики, топ IP) | Выполнены на PostgreSQL 16.15 | Корректный результат, лимит связей соблюдается |
| Фронтенд | `tsc -b` (strict) и `vite build` | Без ошибок |

## Не выполнено в этой среде

- Сборка с реальными пакетами NuGet и `dotnet test` — заглушки проверяют согласованность собственного кода, но не точные сигнатуры сторонних библиотек.
- Генерация миграции EF Core (`scripts/create-migration.*`) и запуск API на реальной PostgreSQL.
- Интеграционные тесты (нужны Docker и пакеты).
- Визуальная проверка интерфейса в браузере.
- Windows Collector на реальном журнале Security.

## Порядок проверки на машине разработчика

```bash
dotnet build LocalSIEM.sln
dotnet test tests/LocalSIEM.Application.Tests
dotnet test tests/LocalSIEM.Api.IntegrationTests
./scripts/dev.sh        # или scripts/dev.ps1
```

Затем — сценарий показа из README, раздел «Демонстрация».
