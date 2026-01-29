# T3 — тестовое задание (C# Web API)

## Что сделано
- Реализован Web API на .NET 8 с двумя эндпоинтами:
  - `POST /api/v1/Transaction`
  - `GET /api/v1/Transaction?id=...`
- Идемпотентный POST с поддержкой **строгого режима** (конфигурируется).
- Валидация:
  - `amount > 0`
  - `transactionDate` не в будущем
- Ошибки возвращаются в формате RFC 9457 (Problem Details).
- Ограничение ёмкости: максимум 100 транзакций (409 Conflict).
- Хранилище: PostgreSQL (через Npgsql), с атомарным счётчиком ёмкости.
- Docker Compose для быстрого запуска.
- Автотесты (in-memory, без БД).

## Структура проекта
- `src/T3.Api` — Web API
- `tests/T3.Api.Tests` — автотесты

## Обоснование текущей реализации (кратко)
Публичная модель транзакции оставлена **ровно по заданию**:

```csharp
public sealed record Transaction(Guid Id, DateTime TransactionDate, decimal Amount);
```

`insertDateTime` хранится и используется внутренне (для идемпотентного POST), но не «ломает» публичный контракт.

Почему это лучше:
- Полное соответствие входным условиям задания.
- Чёткое разделение: доменная сущность vs служебные данные хранения.
- Идемпотентность реализуется без изменения публичной модели.

Минусы:
- `DateTime` не хранит offset (возвращается UTC).
- Нужен маппинг между публичной и внутренней моделью.

## Запуск
### Локально
```bash
dotnet run --project src/T3.Api/T3.Api.csproj
```

### Docker Compose
```bash
docker compose up --build
```
API будет доступен по адресу `http://localhost:8081`.

## Тесты
```bash
dotnet test
```

## Конфигурация
- Строка подключения: `ConnectionStrings:Postgres`
- Строгая идемпотентность: `TransactionOptions:StrictIdempotency`
