# T3.Api

Минимальная реализация Web API для задания T3.

## Требования
- .NET 8 SDK
- PostgreSQL (строка подключения по умолчанию в `appsettings.json`)

## Конфигурация
- Строка подключения: `ConnectionStrings:Postgres`
- Строгая идемпотентность: `TransactionOptions:StrictIdempotency`

Примеры:
```bash
# включить строгий режим идемпотентности
DOTNET_TransactionOptions__StrictIdempotency=true dotnet run
```

## Запуск
```bash
dotnet run --project src/T3.Api/T3.Api.csproj
```

Сервис создаёт таблицы при старте (для тестового задания).

## Docker Compose
Из корня репозитория:
```bash
docker compose up --build
```

API будет доступен по адресу `http://localhost:8081`.

## Эндпоинты
- `POST /api/v1/Transaction`
- `GET /api/v1/Transaction?id=...`

## Формат ошибок
Ошибки возвращаются в формате RFC 9457 (Problem Details).

## Тесты
```bash
dotnet test
```
