# AGENTS — QUY CHUẨN VẬN HÀNH CHO AI CODING AGENTS (NOTIFICATION SERVICE)

> **Trạng thái triển khai (2026-10-24):** ⚠️ MỘT PHẦN — service chỉ có `GET /health` + **1 consumer** không ghi DB; **0/4 rpc**, **0 controller**, **1/9 event**. Các quy tắc dưới đây là chuẩn bắt buộc, nhiều mục **chưa đạt** trong code hiện tại.
> **Nguồn sự thật:** code (`Notification.Api/Program.cs:49`, `Notification.Infrastructure/Consumers/SendOtpEmailConsumer.cs:9`, `Notification.Infrastructure/Persistence/AppDbContext.cs:14-23`).
> **Cập nhật lần cuối:** 2026-10-24.

## 0. HIỆN TRẠNG ĐÃ KIỂM CHỨNG (ĐỌC TRƯỚC KHI LÀM BẤT CỨ GÌ)

| Hạng mục | Hiện trạng | Trạng thái | Bằng chứng (file:line) |
|:---|:---|:---:|:---|
| Điểm vào HTTP | Chỉ `GET /health` (trả hằng số, không kiểm tra DB) | ⚠️ MỘT PHẦN | `Notification.Api/Program.cs:49` |
| gRPC server | Không có `*.proto`, không `Grpc.AspNetCore`, không `MapGrpcService` | ❌ CHƯA CÓ | `Notification.Api.csproj:15-18`; `Program.cs:9-49` |
| Consumer RabbitMQ | 1/9 sự kiện (`SendOtpEmailIntegrationEvent`) | ⚠️ MỘT PHẦN | `Infrastructure/Consumers/SendOtpEmailConsumer.cs:9` |
| Ghi DB từ consumer | **Không** — chỉ gọi email sender + log | ❌ CHƯA CÓ | `:11-45` |
| Kiểm tra preference trước khi gửi | **Không có dòng code nào** | ❌ CHƯA CÓ | 0 truy vấn `NotificationPreferences` |
| Retry / DLQ | Không cấu hình; outbox có bật nhưng chưa kiểm chứng | ❌ CHƯA CÓ | `Infrastructure/DependencyInjection.cs:37-41,43-58` |
| Email khi thiếu credential | Log `[EMAIL SIMULATION]` rồi `return true` ⇒ lỗi bị báo thành công | ⚠️ RỦI RO | `Services/Email/MailKitEmailSenderService.cs:34-41` |
| Test | 2 case, không chạm DB | ⚠️ MỘT PHẦN | `Notification.Tests/EmailSenderTests.cs:18,40` |
| Port / DB | `8007` HTTP/1.1, `notification_db` @ `5439` | ✅ CÓ | `Dockerfile:4`; `appsettings.json:10` |
| CI | Chỉ Docker buildx, không build/test .NET | ❌ CHƯA CÓ | `.github/workflows/ci-cd.yml:38-47` |

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **NotificationService** (455 symbols, 581 relationships, 4 execution flows). Use the GitNexus MCP tools to understand code, assess impact, and navigate safely.

> Index stale? Run `node .gitnexus/run.cjs analyze` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? `npx gitnexus analyze` (npm 11 crash → `npm i -g gitnexus`; #1939).

## Always Do

- **MUST run impact analysis before editing any symbol.** Before modifying a function, class, or method, run `impact({target: "symbolName", direction: "upstream"})` and report the blast radius (direct callers, affected processes, risk level) to the user.
- **MUST run `detect_changes()` before committing** to verify your changes only affect expected symbols and execution flows. For regression review, compare against the default branch: `detect_changes({scope: "compare", base_ref: "main"})`.
- **MUST warn the user** if impact analysis returns HIGH or CRITICAL risk before proceeding with edits.
- When exploring unfamiliar code, use `query({search_query: "concept"})` to find execution flows instead of grepping. It returns process-grouped results ranked by relevance.
- When you need full context on a specific symbol — callers, callees, which execution flows it participates in — use `context({name: "symbolName"})`.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method without first running `impact` on it.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit changes without running `detect_changes()` to check affected scope.

## Resources

| Resource | Use for |
|----------|---------|
| `gitnexus://repo/NotificationService/context` | Codebase overview, check index freshness |
| `gitnexus://repo/NotificationService/clusters` | All functional areas |
| `gitnexus://repo/NotificationService/processes` | All execution flows |
| `gitnexus://repo/NotificationService/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
|------|---------------------|
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->

---

# QUY CHUẨN VẬN HÀNH DÀNH CHO AI CODING AGENTS (NOTIFICATION SERVICE)

---

## 🛑 NGUYÊN TẮC 1: BẮT BUỘC LẬP KẾ HOẠCH TRƯỚC KHI HÀNH ĐỘNG (PLAN-FIRST RULE)

Mọi AI Agent khi nhận nhiệm vụ kỹ thuật tại Notification **BẮT BUỘC PHẢI LẬP VÀ TRÌNH BÀY PLAN** trước khi thực hiện bất kỳ hành động sửa đổi mã nguồn, thêm/xóa file, refactor, tạo migration database hay thay đổi cấu hình nào.

Một Plan chuẩn bắt buộc phải có:
1. **Mục tiêu (Objective)**: Tóm tắt chính xác điều cần làm.
2. **Context & Files cần đọc**: Liệt kê các file nội bộ của Notification cần đọc theo đúng ma trận đọc file tại Nguyên tắc 3.
3. **Phân tích rủi ro & Phạm vi ảnh hưởng (Impact Analysis)**:
   - Dùng GitNexus `impact` đánh giá blast radius các symbol bị ảnh hưởng.
   - Cảnh báo rõ nếu mức độ rủi ro là HIGH hoặc CRITICAL.
4. **Checklist các bước thực hiện (Step-by-Step Implementation)**: Từng bước logic, chi tiết.
5. **Kế hoạch kiểm chứng (Verification)**: `dotnet build Notification.sln -c Release` và `dotnet test Notification.sln -c Release`.

---

## 🧭 NGUYÊN TẮC 2: QUY TẮC PHẠM VI NỘI BỘ (INWARD-ONLY / SELF-CONTAINED)

Notification là một Git repository độc lập (submodule của `Nexus-Be`):
- **TUYỆT ĐỐI CHỈ ĐƯỢC trỏ tới các tài nguyên nội bộ của Notification**.
- **TUYỆT ĐỐI KHÔNG ĐƯỢC trỏ ngược ra ngoài repo** (thư mục kế hoạch/audit của root) hay sang service khác (Catalog, User, Wallet…).
- Toàn bộ tài liệu, hướng dẫn và cấu hình trong repo này phải 100% tự cung tự cấp (Self-contained) — kể cả bảng trạng thái: chỉ trích `file:line` **trong repo này**.

---

## 📚 NGUYÊN TẮC 3: DANH MỤC FILE NỘI BỘ BẮT BUỘC PHẢI ĐỌC TẠI NOTIFICATION

Khi bắt đầu làm việc tại Notification, AI Agent **BẮT BUỘC PHẢI ĐỌC** các file nội bộ sau theo thứ tự:

| # | File | Nội dung cần nắm | Trạng thái | Bằng chứng (file:line) |
|:--|:---|:---|:---:|:---|
| 1 | `README.md` | Tổng quan repo, bảng tính năng và trạng thái thật | ⚠️ MỘT PHẦN | `README.md` §2.1 |
| 2 | `specs/00_AGENT_MISSION_GUARDRAILS.md` | Bounded context thông báo in-app, email SMTP, port `8007`, DB `5439` (`notification_db`) | ⚠️ MỘT PHẦN | 4/6 nhiệm vụ ❌ |
| 3 | `specs/01_CLEAN_ARCHITECTURE_LAYERS.md` | 4 tầng Clean Architecture, No-Repository pattern over EF Core | ✅ CÓ | `Notification.sln:4-13` |
| 4 | `specs/02_API_GRPC_CONTRACTS.md` | Hợp đồng đích `NotificationApi`: `ListNotifications`, `MarkNotificationRead`, `GetPreferences`, `UpdatePreferences` — **0/4 đã implement** | ❌ CHƯA CÓ | `Program.cs:49` |
| 5 | `specs/03_EVENT_MESSAGING.md` | Sự kiện RabbitMQ nhận vào (1/9) — chưa có publisher | ⚠️ MỘT PHẦN | `SendOtpEmailConsumer.cs:9` |
| 6 | `specs/04_DATA_PERSISTENCE.md` | Schema PostgreSQL: 10 entity ↔ 13 bảng (`Notifications`, `NotificationPreferences`, …) | ⚠️ MỘT PHẦN | `AppDbContext.cs:14-23` |
| 7 | `specs/05_DEV_VERIFICATION_CHECKLIST.md` | Lệnh build & test kiểm chứng service | ⚠️ MỘT PHẦN | 2 test case |

---

## 🔒 NGUYÊN TẮC 4: RÀO CHẮN BẤT BIẾN TẠI NOTIFICATION

1. **TUYỆT ĐỐI KHÔNG DÙNG REPOSITORY PATTERN BỌC NGOÀI EF CORE**: dùng trực tiếp `AppDbContext` qua interface `INotificationDbContext` (`Application/Interfaces/INotificationDbContext.cs:8-21`) và `DbSet<T>`. Cấm tạo `INotificationRepository`, `UnitOfWork`. (`NotificationDbContext` trong `Persistence/AppDbContext.cs:38-43` là lớp bọc **chết**, không dùng.)
2. **GỬI THÔNG BÁO BẤT ĐỒNG BỘ ĐA KÊNH**: đích là in-app lưu DB + email qua SMTP/MailKit. **Hiện trạng:** ⚠️ chỉ email được gọi; in-app **không** được ghi; chưa có adapter push/SMS dù enum `ChannelProvider` tồn tại.
3. **CẤU HÌNH PORT 8007 VÀ DATABASE 5439 LÀ BẤT BIẾN**. Service chỉ mở **HTTP/1.1** (`Dockerfile:4`); muốn gRPC phải thêm HTTP/2 endpoint — việc này cần thay đổi có chủ đích, không tự ý sửa.
4. **CẤM SỬ DỤNG gRPC CHO CÁC DỊCH VỤ ĐỒNG BỘ - BẮT BUỘC DÙNG 100% RESTFUL API**:
   - Toàn bộ giao tiếp đồng bộ giữa API Gateway và NotificationService (hoặc giữa các microservices) bắt buộc phải sử dụng chuẩn **RESTful Web API** (JSON qua HTTP/1.1 hoặc HTTP/2).
   - Tuyệt đối **CẤM** triển khai hoặc sử dụng gRPC Server/Client cho các dịch vụ đồng bộ trong toàn bộ hệ thống.
   - Bề mặt API chính thức của NotificationService là REST `/api/v1/notifications/*`. Cấm tạo hoặc sử dụng các file Protobuf (`.proto`).
5. **KHÔNG COI `/health` LÀ BẰNG CHỨNG SERVICE HOẠT ĐỘNG**: handler trả hằng số, không kiểm tra PostgreSQL/RabbitMQ (`Program.cs:49`).
6. **KHÔNG BỊA TRẠNG THÁI**: mọi tuyên bố về rpc/event/bảng phải kèm `file:line` và nhãn ✅/⚠️/❌ theo quy ước ở `README.md`.
7. **CẤM TỰ Ý SỬA ĐỔI BIẾN MÔI TRƯỜNG, SECRET VÀ KEY BÍ MẬT (MANDATORY HUMAN-IN-THE-LOOP FOR SECRETS)**:
   - **Tuyệt đối CẤM mọi AI Coding Agent tự ý sửa đổi, thay thế, xóa hoặc ghi đè các biến môi trường (`.env`, `.env.*`), Connection Strings, API Keys, JWT Secrets, chứng chỉ hoặc bất kỳ thông tin bí mật (credentials/secrets) nào trong toàn bộ hệ thống.**
   - **Bắt buộc phải xin ý kiến và nhận được sự đồng ý rõ ràng (explicit approval) từ người dùng trước khi thực hiện bất kỳ thay đổi nào liên quan đến biến môi trường hoặc tài khoản bí mật.**
   - Khi phát hiện cấu hình môi trường, connection string hoặc secret key có vấn đề hoặc cần cập nhật, Agent **CHỈ ĐƯỢC PHÉP báo cáo hiện trạng, đề xuất phương án và chờ người dùng xác nhận**, tuyệt đối không tự ý can thiệp.

---

## 💬 NGUYÊN TẮC 5: QUY CHUẨN THÔNG BÁO LỖI TOÀN HỆ THỐNG (MANDATORY ERROR RULES)

Tất cả các thông báo lỗi (Error Messages, Exceptions, ProblemDetails, Validation Errors, Response Details) **BẮT BUỘC** phải tuân theo 3 nguyên tắc sau:
1. **Ngôn ngữ (100% English)**: Toàn bộ thông báo lỗi phải được viết bằng tiếng Anh. Tuyệt đối không dùng tiếng Việt trong chuỗi thông báo lỗi trả về cho client.
2. **Quy cách viết hoa (Sentence case)**: Viết hoa duy nhất chữ cái đầu câu, các từ tiếp theo viết thường (ngoại trừ tên riêng, từ viết tắt hoặc định danh kỹ thuật như `ID`, `OTP`, `JWT`, `URL`, `UUID`, `Email`...).
   - ✅ Đúng: `"Notification not found or does not belong to the user."`
   - ✅ Đúng: `"Recipient email address is invalid."`
   - ❌ Sai: `"Thông báo không tồn tại"` (Không dùng tiếng Việt)
   - ❌ Sai: `"notification not found"` (Chữ cái đầu không viết hoa)
   - ❌ Sai: `"NOTIFICATION NOT FOUND"` (Cấm viết hoa toàn bộ)
   - ❌ Sai: `"Notification Not Found Provided"` (Không dùng Title Case)
3. **Mã lỗi (Error Codes — SCREAMING_SNAKE_CASE)**: Mã lỗi luôn viết hoa toàn bộ và nối bằng dấu gạch dưới:
   - Ví dụ: `NOTIFICATION_NOT_FOUND`, `INVALID_EMAIL`, `VAL_INVALID_INPUT`, `UNAUTHORIZED`, `FORBIDDEN`.
   - Mã đang có trong code: `NOT_FOUND`, `BUSINESS_RULE_VIOLATION`, `DOMAIN_ERROR`, `VAL_INVALID_ARGUMENT`, `INTERNAL_SERVER_ERROR` (`Notification.Api/Middleware/GlobalExceptionHandlerMiddleware.cs:40,45,50,55,60`).

---

## 🎯 NGUYÊN TẮC 6: ĐIỀU KIỆN HOÀN THÀNH NHIỆM VỤ (DEFINITION OF DONE)

Một task tại Notification chỉ hoàn tất khi:
- [ ] Đã lập Plan chi tiết trước khi thực hiện.
- [ ] Không chứa bất kỳ liên kết trỏ ngược ra ngoài Notification.
- [ ] `dotnet build Notification.sln -c Release` thành công 100% (0 error).
- [ ] `dotnet test Notification.sln -c Release` vượt qua 100% các bài test.
- [ ] Không thêm/xóa bảng hay rpc mà chưa cập nhật `specs/02`, `specs/04` và bảng trạng thái trong `README.md`.
- [ ] Trạng thái Git sạch, không commit file rác (`bin/`, `obj/`, `.vs/`, `.idea/`).
