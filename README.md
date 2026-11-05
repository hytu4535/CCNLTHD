# Stripe Webhooks với ASP.NET Core

Đây là dự án tham khảo triển khai Stripe Webhook đáng tin cậy bằng ASP.NET
Core Minimal API, .NET 8, Entity Framework Core, PostgreSQL và Docker.

Dự án có quy mô nhỏ nhưng minh họa các yêu cầu quan trọng khi xử lý thanh toán:

- Xác thực chữ ký Stripe bằng request body gốc.
- Xử lý idempotency theo mã sự kiện Stripe (`evt_...`).
- Lưu dữ liệu và xử lý nghiệp vụ trong cùng một transaction.
- API demo cho PaymentIntent, refund, sản phẩm và đơn hàng.
- Migration PostgreSQL và health check.
- Logging có cấu trúc với Serilog và Seq.
- Unit test và integration test ở cấp độ API.

> **Phạm vi:** Đây là dự án học tập và tham khảo, không phải một nền tảng thanh
> toán hoàn chỉnh. Cần đánh giá lại bảo mật, logging, giám sát và cấu hình triển
> khai trước khi sử dụng trong môi trường production.

## Mục lục

- [Kiến trúc](#kiến-trúc)
- [Công nghệ sử dụng](#công-nghệ-sử-dụng)
- [Yêu cầu môi trường](#yêu-cầu-môi-trường)
- [Cấu hình](#cấu-hình)
- [Chạy nhanh](#chạy-nhanh)
- [Chạy bằng Docker](#chạy-bằng-docker)
- [Chạy API trên máy local](#chạy-api-trên-máy-local)
- [Stripe CLI và kiểm thử webhook](#stripe-cli-và-kiểm-thử-webhook)
- [Danh sách API](#danh-sách-api)
- [Cơ chế xử lý webhook](#cơ-chế-xử-lý-webhook)
- [Kiểm thử](#kiểm-thử)
- [Postman](#postman)
- [Cấu trúc dự án](#cấu-trúc-dự-án)
- [Xử lý sự cố](#xử-lý-sự-cố)
- [Lưu ý bảo mật](#lưu-ý-bảo-mật)
- [Tài liệu liên quan](#tài-liệu-liên-quan)

## Kiến trúc

Ứng dụng cung cấp Minimal API, lưu dữ liệu nghiệp vụ và thông tin webhook vào
PostgreSQL.

```text
Stripe
  │
  │  POST /webhooks/stripe có chữ ký
  ▼
ASP.NET Core API
  ├── Xác thực Stripe-Signature
  ├── Kiểm tra ProcessedEvents theo evt_...
  ├── Lưu PaymentEvent
  ├── Cập nhật đơn hàng liên quan
  └── Commit transaction
          │
          ├── PostgreSQL
          └── Seq
```

### Quy tắc xử lý webhook

1. Endpoint yêu cầu header `Stripe-Signature`.
2. API đọc request body gốc trước khi xác thực chữ ký.
3. Chữ ký được xác thực bằng `Stripe:WebhookSecret`.
4. Mã sự kiện được kiểm tra trong kho idempotency.
5. Sự kiện mới và tác động nghiệp vụ được lưu trong cùng một transaction.
6. Sự kiện xử lý thành công hoặc bị trùng đều trả về HTTP `200`.
7. Request không hợp lệ trả về `400` hoặc `401`.
8. Lỗi xử lý trả về `500` để Stripe có thể gửi lại sự kiện.

Các loại sự kiện hiện được xử lý:

- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `refund.created`
- `refund.updated`
- `charge.refunded`

Xem thêm:

- [Kiến trúc](docs/10-architecture.md)
- [Idempotency của webhook](docs/20-webhook-idempotency.md)

## Công nghệ sử dụng

| Thành phần | Công nghệ / phiên bản |
|---|---|
| Runtime | .NET 8 |
| Web framework | ASP.NET Core Minimal APIs |
| ORM | Entity Framework Core 8 |
| Cơ sở dữ liệu | PostgreSQL 16 |
| Stripe SDK | Stripe.net 48.5.0 |
| Logging | Serilog và Seq |
| API documentation | Swagger / OpenAPI |
| Test framework | xUnit, FluentAssertions, ASP.NET Core Test Host |
| Database dùng trong test | SQLite in-memory |
| Container | Docker Compose |

## Yêu cầu môi trường

Cài đặt các công cụ sau:

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) có hỗ trợ
  Docker Compose
- [Stripe CLI](https://stripe.com/docs/stripe-cli)
- Tài khoản Stripe ở test mode và API key tương ứng

Kiểm tra cài đặt:

```bash
dotnet --version
docker --version
docker compose version
stripe --version
```

## Cấu hình

Ứng dụng sử dụng cơ chế cấu hình chuẩn của ASP.NET Core. Không commit Stripe
key, webhook secret hoặc mật khẩu cơ sở dữ liệu thật vào Git.

### Cấu hình khi chạy local

Chỉnh sửa [appsettings.Development.json](src/StripeWebhooks.Api/appsettings.Development.json)
hoặc cung cấp các biến môi trường tương đương:

```json
{
  "ConnectionStrings": {
    "Db": "Host=localhost;Port=55432;Database=stripe_webhooks_dotnet;Username=postgres;Password=postgres"
  },
  "Stripe": {
    "SecretKey": "sk_test_your_key",
    "WebhookSecret": "whsec_your_cli_secret",
    "SkipSignatureValidation": false
  }
}
```

Các cấu hình chính:

| Cấu hình | Mô tả |
|---|---|
| `ConnectionStrings:Db` | Connection string đến PostgreSQL |
| `Stripe:SecretKey` | Secret API key của Stripe |
| `Stripe:WebhookSecret` | Secret do lệnh `stripe listen` in ra |
| `Stripe:SkipSignatureValidation` | Giữ là `false`; chỉ bypass trong test local có kiểm soát |
| `Serilog:WriteTo` | Cấu hình log ra Console và Seq |

Tên biến môi trường dùng dấu gạch dưới kép:

```text
ConnectionStrings__Db=Host=localhost;Port=55432;Database=stripe_webhooks_dotnet;Username=postgres;Password=postgres
Stripe__SecretKey=sk_test_...
Stripe__WebhookSecret=whsec_...
Stripe__SkipSignatureValidation=false
```

### Cấu hình khi chạy Docker

Sao chép file cấu hình mẫu:

```bash
cp .env.runtime.example .env.runtime
```

Trên PowerShell:

```powershell
Copy-Item .env.runtime.example .env.runtime
```

Chỉnh sửa `.env.runtime` để thêm Stripe test key và webhook secret. File này
được Git bỏ qua và không được commit.

## Chạy nhanh

Đây là cách ngắn nhất để chạy môi trường local:

```bash
# 1. Khởi động PostgreSQL và Seq
docker compose -f docker-compose.dev.yml up -d

# 2. Cấu hình appsettings.Development.json

# 3. Khởi động API
dotnet run --project src/StripeWebhooks.Api

# 4. Mở terminal khác để forward sự kiện Stripe
stripe login
stripe listen --events payment_intent.succeeded --forward-to http://localhost:7020/webhooks/stripe

# 5. Mở terminal thứ ba để tạo sự kiện test
stripe trigger payment_intent.succeeded
```

Các địa chỉ truy cập:

- API: <http://localhost:7020/>
- Swagger UI: <http://localhost:7020/swagger>
- Health check: <http://localhost:7020/health>
- Seq: <http://localhost:5341>

Lệnh `stripe listen` sẽ in ra một secret dạng `whsec_...`. Sao chép secret này
vào `Stripe:WebhookSecret` trước khi tạo sự kiện test.

## Chạy bằng Docker

### Khởi động toàn bộ stack

```bash
cp .env.runtime.example .env.runtime
# Chỉnh sửa .env.runtime để thêm Stripe credentials
docker compose up -d --build
```

Các service được khởi động:

| Service | Địa chỉ | Chức năng |
|---|---|---|
| `api` | <http://localhost:7020> | ASP.NET Core API |
| `db` | `localhost:55432` | PostgreSQL |
| `seq` | <http://localhost:5341> | Xem log có cấu trúc |
| `pgadmin` | <http://localhost:5050> | Giao diện quản trị database, tùy chọn |

Container API tự chạy EF Core migration khi khởi động. PostgreSQL phải vượt qua
health check trước khi API được khởi động.

Khởi động thêm pgAdmin:

```bash
docker compose --profile tools up -d
```

Thông tin đăng nhập pgAdmin mặc định trong Compose:

```text
Email:    admin@admin.com
Password: root
```

### Chỉ khởi động hạ tầng

Dùng file Compose development khi chạy API bằng Visual Studio, VS Code hoặc
`dotnet run`:

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet run --project src/StripeWebhooks.Api
```

Dừng hạ tầng:

```bash
docker compose -f docker-compose.dev.yml down
```

Dừng toàn bộ stack:

```bash
docker compose down
```

Chỉ dùng `-v` khi muốn xóa toàn bộ volume PostgreSQL, Seq, pgAdmin và dữ liệu
local đã lưu.

## Chạy API trên máy local

Khôi phục package, build solution và chạy API:

```bash
dotnet restore stripe-webhooks-dotnet.sln
dotnet build stripe-webhooks-dotnet.sln
dotnet run --project src/StripeWebhooks.Api
```

Ngoài môi trường `Testing`, ứng dụng sẽ tự chạy các migration EF Core còn thiếu
khi khởi động. Đảm bảo PostgreSQL đang hoạt động và connection string hợp lệ.

## Stripe CLI và kiểm thử webhook

Khởi động listener và giữ terminal này chạy:

```bash
stripe login
stripe listen --events payment_intent.succeeded --forward-to http://localhost:7020/webhooks/stripe
```

Tạo một số sự kiện mẫu:

```bash
stripe trigger payment_intent.succeeded
stripe trigger payment_intent.payment_failed
stripe trigger charge.refunded
```

URL forward phải chính xác là:

```text
http://localhost:7020/webhooks/stripe
```

Không gọi webhook bằng request JSON thông thường. Endpoint xác thực chữ ký
Stripe và sẽ từ chối request không có chữ ký hợp lệ.

## Danh sách API

Swagger tại <http://localhost:7020/swagger> là tài liệu API tương tác chính
thức.

### API hệ thống

| Method | Route | Mô tả |
|---|---|---|
| `GET` | `/` | Kiểm tra trạng thái service |
| `GET` | `/health` | Health check của database |
| `GET` | `/swagger` | Giao diện Swagger |
| `POST` | `/webhooks/stripe` | Nhận sự kiện Stripe có chữ ký |

### Sản phẩm

| Method | Route | Mô tả |
|---|---|---|
| `GET` | `/api/products` | Danh sách sản phẩm, hỗ trợ query `search` |
| `GET` | `/api/products/{id}` | Lấy một sản phẩm |
| `POST` | `/api/products` | Tạo sản phẩm |
| `PUT` | `/api/products/{id}` | Cập nhật sản phẩm |
| `DELETE` | `/api/products/{id}` | Xóa sản phẩm |

Ví dụ:

```bash
curl -X POST http://localhost:7020/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Coffee","price":4.50,"stockQuantity":100}'
```

### Đơn hàng

| Method | Route | Mô tả |
|---|---|---|
| `GET` | `/api/orders` | Danh sách đơn hàng |
| `GET` | `/api/orders/{id}` | Lấy một đơn hàng |
| `POST` | `/api/orders` | Tạo đơn hàng |
| `PUT` | `/api/orders/{id}` | Cập nhật đơn hàng |
| `DELETE` | `/api/orders/{id}` | Xóa đơn hàng |

Tạo đơn hàng sau khi đã tạo sản phẩm và Stripe PaymentIntent:

```json
{
  "productId": 1,
  "quantity": 2,
  "paymentIntentId": "pi_123456789"
}
```

Đơn hàng bắt đầu ở trạng thái `Pending`. Sự kiện PaymentIntent thành công hoặc
thất bại sẽ cập nhật trạng thái thanh toán thành `Paid` hoặc `Failed`.

### API PaymentIntent demo

Các endpoint dưới đây gọi trực tiếp Stripe và chỉ dành cho demo local:

| Method | Route | Mô tả |
|---|---|---|
| `POST` | `/demo/payment-intents` | Tạo PaymentIntent |
| `POST` | `/demo/payment-intents/{id}/confirm` | Xác nhận PaymentIntent |
| `POST` | `/demo/payment-intents/{id}/refund` | Hoàn tiền toàn bộ hoặc một phần |
| `GET` | `/demo/payment-events?take=50` | Xem payment event đã lưu |

Nên truyền header `Idempotency-Key` khi lặp lại thao tác tạo, xác nhận hoặc
refund:

```bash
curl -X POST http://localhost:7020/demo/payment-intents \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-payment-001" \
  -d '{"amount":900,"currency":"nzd"}'
```

`amount` được tính theo đơn vị nhỏ nhất của loại tiền. Ví dụ, `900` tương ứng
với 900 cent đối với loại tiền dùng cent.

## Cơ chế xử lý webhook

Dữ liệu webhook được lưu thành hai nhóm bản ghi:

- `ProcessedEvents`: cổng idempotency, định danh bằng mã sự kiện Stripe.
- `PaymentEvents`: lịch sử sự kiện và metadata của payload.

Khi Stripe gửi lại cùng một sự kiện, mã `evt_...` đã tồn tại sẽ được phát hiện
và tác động nghiệp vụ không bị chạy lần thứ hai. Trả về `200` cho sự kiện
trùng để thông báo Stripe rằng delivery đã được xử lý.

Nếu xử lý thất bại, service trả về `500` thay vì xác nhận thành công. Stripe có
thể gửi lại sự kiện sau đó.

API cũng tạo correlation ID và request log có cấu trúc. Trong Docker, API gửi log
đến `http://seq:80`; từ máy host có thể xem Seq tại
<http://localhost:5341>.

## Kiểm thử

Chạy toàn bộ test:

```bash
dotnet test stripe-webhooks-dotnet.sln
```

Test suite bao gồm:

- Unit test xác thực chữ ký.
- Test idempotency và sự kiện trùng.
- Test health và infrastructure.
- Integration test cho Product và Order API.
- Integration test cho Stripe Webhook API.

Integration test chạy toàn bộ ASP.NET Core request pipeline thông qua
`WebApplicationFactory` và dùng SQLite in-memory. Không cần Docker, PostgreSQL,
Stripe credentials hoặc Stripe CLI để chạy test.

Xem thêm [docs/50-testing.md](docs/50-testing.md).

## Postman

Repository có sẵn các file để import vào Postman:

- [Collection](postman/stripe-webhooks-dotnet.postman_collection.json)
- [Environment](postman/stripe-webhooks-dotnet.postman_environment.json)

Import cả hai file, chọn environment và đặt base URL:

```text
http://localhost:7020
```

## Cấu trúc dự án

```text
.
├── src/StripeWebhooks.Api
│   ├── DTOs/                 DTO request và response
│   ├── Endpoints/            Nhóm route Minimal API
│   ├── Infrastructure/      Middleware và service hạ tầng
│   ├── Migrations/           EF Core database migration
│   ├── Models/               Domain model
│   ├── Persistence/          DbContext và entity lưu trữ
│   └── Stripe/               Xác thực chữ ký và xử lý webhook
├── tests/StripeWebhooks.Tests
│   ├── Integration/          Test toàn bộ API pipeline
│   ├── Unit/                 Unit test
│   └── Utils/                Test host và helper tạo chữ ký
├── docs/                     Tài liệu thiết kế và vận hành
├── postman/                  Postman collection và environment
├── docker-compose.yml        Stack API, PostgreSQL và Seq
├── docker-compose.dev.yml    Stack chỉ gồm hạ tầng
├── Dockerfile                Multi-stage build cho API container
└── stripe-webhooks-dotnet.sln
```

## Xử lý sự cố

### `/health` không healthy

Kiểm tra PostgreSQL đang chạy và host trong connection string đúng:

- API chạy trên máy host: dùng `Host=localhost;Port=55432`.
- API chạy trong Docker: dùng `Host=db;Port=5432`.

Xem log container:

```bash
docker compose logs -f db api
```

### Không có webhook event được lưu

Kiểm tra:

1. `stripe listen` vẫn đang chạy.
2. URL forward là `http://localhost:7020/webhooks/stripe`.
3. `Stripe:WebhookSecret` khớp chính xác secret từ Stripe CLI.
4. API đang chạy ở port `7020`.
5. Sự kiện được tạo trong Stripe test mode.

### Xác thực chữ ký thất bại

Sử dụng secret từ đúng tiến trình `stripe listen` hiện tại. Secret từ một phiên
listener cũ có thể không còn hợp lệ. Giữ
`Stripe:SkipSignatureValidation` là `false` trong quá trình phát triển bình
thường.

### API Docker không kết nối được Seq

Từ trong Docker phải dùng `http://seq:80`, không dùng
`http://localhost:5341`. `localhost` bên trong container API trỏ về chính
container API.

### Port đã được sử dụng

Đổi port phía host trong file Compose tương ứng hoặc dừng tiến trình đang sử
dụng port `7020`, `55432`, `5341` hoặc `5050`.

## Lưu ý bảo mật

- Chỉ dùng test key của Stripe ở local; lưu live key trong secret manager.
- Không commit `.env.runtime`, API key, webhook secret hoặc database password.
- Luôn bật xác thực chữ ký webhook.
- Xác thực chữ ký trên raw request body.
- Giới hạn quyền truy cập database và Seq ở môi trường không phải local.
- Bổ sung authentication và authorization trước khi public các endpoint sản
  phẩm, đơn hàng hoặc demo.
- Kiểm tra log để không làm lộ dữ liệu nhạy cảm hoặc thông tin thanh toán.

## Tài liệu liên quan

- [Quick start](docs/00-quickstart.md)
- [Kiến trúc](docs/10-architecture.md)
- [Idempotency webhook](docs/20-webhook-idempotency.md)
- [Stripe CLI local development](docs/30-local-dev-stripe-cli.md)
- [Troubleshooting](docs/40-troubleshooting.md)
- [Testing](docs/50-testing.md)

## Giấy phép

Xem [LICENSE](LICENSE).
