# Đồng bộ `dev` ngày 28/11/2026

Đã fetch `origin/dev` của 11 repo trong `D:\OJT_FPT\Nexus_Project`. Những checkout sạch và merge không xung đột đã cập nhật; nơi có sửa đổi local hoặc conflict được giữ nguyên, audit dùng cây `origin/dev` mới fetch. Không reset, xóa stash hoặc ghi đè code local.

| Repo | Commit `origin/dev` đã fetch | Trạng thái worktree sau thử đồng bộ |
|---|---|---|
| AuctionService | `a146d7d` | Merge thành công; HEAD `a3e496d`, còn 1 thay đổi local |
| CatalogService | `3b433ea` | Merge thành công; HEAD `3a168ab` |
| CommerceService | `fec2f84` | Chưa merge do sửa đổi local trùng `OrderService`; đang sau remote 8 commit |
| FulfillmentService | `383e333` | Merge thành công; HEAD `e6d1a67` |
| Gateway | `cbc9f56` | Chưa merge do sửa đổi local trùng `docker-compose.yml`; sau remote 10 commit |
| LogService | `a0d237a` | Đồng bộ xong |
| Nexus FE | `34becc9` | Thử merge xung đột `RoleGate.tsx`, `AuthScreens.tsx`; đã abort, giữ nguyên local; sau remote 23 commit |
| NotificationService | `a900fb5` | Chưa merge do sửa đổi local trùng enum/test; sau remote 11 commit |
| UserService | `6e775ee` | Thử merge xung đột `EmailNotificationClient.cs`; đã abort, giữ nguyên local; sau remote 9 commit |
| WalletService | `a4cf3ba` | Đồng bộ xong |
| BE root | `da0a4b4` | Đồng bộ xong; submodule pointer local cần đối chiếu trước commit |

`origin/dev` là mốc để đọc báo cáo, không phải cam kết tất cả worktree đang ở mốc đó. Trước khi build chung: lập nhánh tích hợp riêng, rà diff/stash, giải quyết conflict từng repo, test hợp đồng caller/callee, rồi mới cập nhật pointer submodule root. Không chọn tự động một phía cho code nghiệp vụ.
