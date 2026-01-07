# API Gateway (BFF) Project

## Описание

Проект реализует **API Gateway / Backend-for-Frontend (BFF)** для микросервисной системы. Gateway объединяет данные из нескольких микросервисов и предоставляет единый REST API с поддержкой:

* Агрегации данных
* Кэширования (Redis)
* JWT-аутентификации
* Rate limiting
* Fallback / retry (Polly)
* Мониторинга (Prometheus + Grafana)

Микросервисы проекта:

1. **User Service** — хранит информацию о пользователях.
2. **Order Service** — хранит список заказов пользователя.
3. **Product Service** — хранит данные о товарах.
4. **API Gateway / BFF** — объединяет данные и предоставляет единое REST API клиентам.

## Стек технологий

* Язык: **C# (.NET 8)**
* Фреймворк: **ASP.NET Core Minimal API**
* Кэширование: **Redis**
* JWT аутентификация: **Microsoft.IdentityModel.Tokens**
* Роутинг: **YARP / Minimal API**
* Retry / Circuit Breaker: **Polly**
* Мониторинг: **Prometheus + Grafana**
* Логирование: **Serilog**
* Контейнеризация: **Docker Compose**
* HTTP клиенты: **HttpClientFactory с Polly**

## Сценарий использования

### 1. Авторизация через JWT

Endpoint: `POST /auth/login`

**Request:**

```json
{
  "username": "admin",
  "password": "password"
}
```

**Response (200 OK):**

```json
{
  "access_token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

> Этот токен используется для доступа к защищённым endpoint-ам.

### 2. Получение профиля пользователя с заказами и товарами

Endpoint: `GET /api/profile/{userId}`

**Headers:**

```
Authorization: Bearer <access_token>
```

**Response (200 OK):**

```json
{
  "user": {
    "id": "1",
    "name": "Ivan Ivanov",
    "email": "ivan@mail.com"
  },
  "orders": [
    {
      "orderId": "101",
      "productId": "A1",
      "quantity": 2,
      "product": {
        "id": "A1",
        "name": "Laptop",
        "price": 1200
      }
    },
    {
      "orderId": "102",
      "productId": "B2",
      "quantity": 1,
      "product": {
        "id": "B2",
        "name": "Mouse",
        "price": 50
      }
    }
  ]
}
```

* Результат кэшируется на **30 секунд** в Redis.
* Gateway объединяет данные из 3 сервисов: **Users**, **Orders**, **Products**.

## Установка и запуск

### 1. Клонировать репозиторий

```bash
git clone <repository_url>
cd APIGateaway
```

### 2. Запуск через Docker Compose

```bash
docker compose up --build
```

Сервисы будут доступны на следующих портах:

| Сервис          | Порт |
| --------------- | ---- |
| API Gateway     | 8080 |
| User Service    | 5001 |
| Order Service   | 5002 |
| Product Service | 5003 |
| Redis           | 6379 |
| Grafana         | 3000 |
| Prometheus      | 9090 |

> Важно: внутри контейнеров Gateway обращается к микросервисам по имени сервиса и порту внутри контейнера (например, `http://user-service:8080`).

### 3. Доступ к Grafana

* URL: `http://localhost:3000`
* Login / Password: `admin` / `admin`
* Data Source: Prometheus (`http://prometheus:9090`)

**Совет:** импортировать дашборд ASP.NET Core с ID `1860` для отображения метрик Kestrel и HTTP запросов.

### 4. Тестирование API

#### Логин (PowerShell):

```powershell
Invoke-RestMethod `
  -Method POST `
  -Uri http://localhost:8080/auth/login `
  -Headers @{ "Content-Type" = "application/json" } `
  -Body '{ "username": "admin", "password": "password" }'
```

#### Получение профиля пользователя:

```powershell
$token = "<JWT token>"
Invoke-RestMethod `
  -Uri http://localhost:8080/api/profile/1 `
  -Headers @{ "Authorization" = "Bearer $token" }
```

## Микросервисы

### User Service (`/users/{id}`)

Пример ответа:

```json
{
  "id": "1",
  "name": "Ivan Ivanov",
  "email": "ivan@mail.com"
}
```

### Order Service (`/orders/user/{userId}`)

Пример ответа:

```json
[
  { "orderId": "101", "productId": "A1", "quantity": 2 },
  { "orderId": "102", "productId": "B2", "quantity": 1 }
]
```

### Product Service (`/products/{id}`)

Пример ответа:

```json
{
  "id": "A1",
  "name": "Laptop",
  "price": 1200
}
```

## JWT / Auth

* Алгоритм: **HS256**
* Минимальная длина ключа: 32 байта
* Токен действителен: **30 минут**
* Токен нужен для всех защищённых endpoints (`/api/profile/{userId}`)

## Redis Cache

* Используется для кэширования агрегированного ответа `/api/profile/{userId}`
* TTL: 30 секунд

## Rate Limiting

* **Fixed Window** — максимум 5 запросов каждые 10 секунд на endpoint
* Реализовано через `AddRateLimiter`

## Monitoring

* Метрики собираются через **OpenTelemetry**
* Доступно на Prometheus (`/metrics`)
* Отображаются в Grafana
* Метрики включают:

  * `kestrel_active_connections`
  * `http_server_request_duration_seconds`
  * Количество активных запросов

## Logging

* Логирование через **Serilog**
* Логи выводятся в консоль и могут быть настроены через `appsettings.json`

## Возможные улучшения / TODO

* Поддержка GraphQL
* gRPC вызовы между микросервисами
* Swagger с JWT авторизацией
* Refresh tokens для долгоживущих сессий
* Circuit breaker + retry для всех HttpClient вызовов

## Контакты / Примечания

* Разработчик: [Ваше имя]
* Язык проекта: C# (.NET 8)
* Документация: Minimal API, Docker, Prometheus, Grafana

> Проект полностью готов к сдаче и демонстрации работы:
>
> * Авторизация JWT
> * Агрегация данных
> * Кэширование
> * Rate limiting
> * Мониторинг
