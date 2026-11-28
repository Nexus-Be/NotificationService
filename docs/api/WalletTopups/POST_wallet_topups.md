# POST /api/v1/notifications/wallet-topups

Receives a confirmed Stripe top-up from WalletService and creates one logical buyer notice with in-app and email deliveries. Wallet owns payment, wallet balance, transaction, and Stripe verification. Notification does not verify or change money.

| Item | Contract |
|---|---|
| Caller | WalletService only |
| Auth | `X-Internal-Service-Key` matching `WalletNotificationAuth:ServiceKey`; no bearer or user headers |
| Request | `WalletTopupRequest`: `sourceEventId`, `topupId`, `transactionId`, `recipientUserId`, `amount`, `currency`, `confirmedAt`, optional `traceId` |
| Source ID | `wallet-topup:{topupId:D}:succeeded`, stable across webhook replay/reconciliation |
| Recipient | Wallet supplies user ID; email resolved by Notification from UserService at delivery time |
| Response | 200 `WalletTopupResponse` with source ID, notification ID, `isReplay`, and Email/InApp delivery IDs/statuses |
| Errors | 400 `VAL_INVALID_INPUT`; 401 invalid/missing Wallet key; 409 `WALLET_TOPUP_CONFLICT`, `CHANNEL_NOT_FOUND` or `DELIVERY_STATE_INVALID` |
| Persistence | One `NotificationRecord`, two `NotificationDelivery` rows, logs and audit in one transaction; advisory lock by top-up ID |
| Enum | `WalletTopupSucceeded = 33`; earlier integer values unchanged |
| Email | HTML template stored with the delivery; `Pending` until SMTP worker sends; 200 does not mean delivered |

Required amount is an integer from 10,000 to 99,999,999 VND. `confirmedAt` must be no more than 30 seconds in the future. Replaying the same top-up and business data returns the original notification. Changing transaction, user, amount, currency, or confirmed time for the same top-up returns 409. Trace ID and timestamp offset representation may change on retry.

The caller must persist a delivery request with the wallet credit and send over REST after commit. Refer to [the Wallet handoff](../../integrations/WALLET_TOPUP_NOTIFICATION_HANDOFF.md) for the JSON example and Wallet implementation checklist.
