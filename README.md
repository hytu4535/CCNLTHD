# Hệ thống quản lý đơn hàng tích hợp Webhook thanh toán Stripe

Đồ án môn **Các công nghệ lập trình hiện đại**.

Backend **ASP.NET Core (.NET 8)** — Minimal API: quản lý sản phẩm/đơn hàng và nhận webhook thanh toán Stripe (xác thực chữ ký, chống trùng, cập nhật trạng thái đơn).

Repo: https://github.com/hytu4535/CCNLTHD

---

## Công nghệ sử dụng

- ASP.NET Core 8 (Minimal API)
- EF Core + PostgreSQL
- Stripe Webhook (HMAC, idempotency)
- Serilog + CorrelationId
- Swagger / OpenAPI
- Docker + Docker Compose

---

## Tính năng

- CRUD sản phẩm: `/api/products`
- CRUD đơn hàng: `/api/orders` (gắn PaymentIntentId)
- Demo PaymentIntent: `/demo/payment-intents`
- Webhook Stripe: `POST /webhooks/stripe`
- Health check: `GET /health`

---

## Cài đặt trước khi chạy

1. Cài [Docker Desktop](https://www.docker.com/products/docker-desktop) và bật lên.
2. Cài [Stripe CLI](https://stripe.com/docs/stripe-cli), kiểm tra: `stripe --version`
3. Có Stripe Secret key dạng `sk_test_...` (test mode)

---

## Cách chạy


**Bước 1 — Tạo file cấu hình**

```bash
cp .env.runtime.example .env.runtime

Mở .env.runtime, điền:

Stripe__SecretKey=sk_test_...
Stripe__WebhookSecret=whsec_... (lấy ở bước 3)


Bước 2 — Chạy hệ thống
docker compose up -d --build
API: http://localhost:7020
Swagger: http://localhost:7020/swagger
Health: http://localhost:7020/health


Bước 3 — Forward webhook về máy
stripe login
stripe listen --forward-to http://localhost:7020/webhooks/stripe

Copy dòng whsec_... in ra, dán vào .env.runtime, rồi:
docker compose restart api

Bước 4 — Demo nhanh

Vào Swagger, tạo sản phẩm.
Gọi POST /demo/payment-intents, lấy mã pi_....
Tạo đơn POST /api/orders (có productId, quantity, paymentIntentId).
Confirm thanh toán hoặc chạy: stripe trigger payment_intent.succeeded
Xem lại đơn: trạng thái Paid.


Cấu trúc thư mục chính
src/StripeWebhooks.Api/
  Program.cs
  Endpoints/
  Persistence/
  Migrations/
  Stripe/
  Infrastructure/
docker-compose.yml
.env.runtime.example


Xử lý webhook (tóm tắt)

Nhận POST kèm header Stripe-Signature
Kiểm tra chữ ký bằng WebhookSecret
Kiểm tra event đã xử lý chưa (bảng processed_events)
Cập nhật đơn hàng theo payment_intent
Trả về HTTP 200


Lệnh hay dùng
docker compose logs api --tail 50
docker compose restart api
docker compose down
docker compose up -d --build


Bảo mật

Không commit file .env.runtime và các key sk_test_, whsec_
Secret chỉ để trong biến môi trường / file env local


Nhóm thực hiện: 05
Môn Các công nghệ lập trình hiện đại — Trường Đại học Sài Gòn.
