---
description: "Authentication, authorization, rate limiting, exception handling, and response casing conventions"
alwaysApply: true
---
# BidParser - API & Security Hardening Rules

This rule defines security controls, rate limiting, authentication/authorization flows, and DTO/JSON serialization standards for the API.

## 1. Authentication & Authorization

- **Passwords**: Bcrypt cost factor of 12. Enforced on `/auth/change-password` with standard complexity: $\ge 8$ characters, $\ge 1$ uppercase, $\ge 1$ digit, $\ge 1$ symbol.
- **Sessions**: Data Protection cookies (`bidparser_session`), `HttpOnly`, `SameSite=Lax`, hard 12-hour expiry (no sliding window). `Secure` flag is dynamic; set only when `X-Forwarded-Proto=https` is processed.
- **CSRF Guard**: Every non-GET endpoint requires the `X-Requested-With: BidParser` header.
- **`must_change_password` Gate**: When this claim is true, the backend returns `403 password_change_required` for all endpoints except `/auth/*` and `/me`. The frontend guards all routes and forces redirection to the `/change-password` screen.
- **Authorization Policies**:
  - `LoggedIn` — Valid session cookie; ignores the `must_change_password` status. Used for logout, password change, and current user retrieval.
  - `ActiveUser` — LoggedIn AND `must_change_password=false`. Used for standard application actions.
  - `Admin` — ActiveUser AND `role == "admin"`.
- **Last-Admin Safeguard**: User management endpoints must refuse changes or deletions that would leave the system with zero admin users, or target the caller's own admin account.

---

## 2. Rate Limiting & Safety

- **Endpoint Rate Limiting**: Per-user token-bucket policy (`"parse"`) on `/api/parse` (max 10 requests, 5 tokens replenished per minute).
- **Auth Endpoint Protection**: A custom `AuthRateLimiter` restricts attempts to 5 per minute per IP and per username independently.
- **Upload File Integrity**: File uploads must be checked by file extension and validated against magic byte signatures before parsing:
  - `application/pdf` -> `%PDF` (`0x25 0x50 0x44 0x46`)
  - `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` -> `PK\x03\x04` (`0x50 0x4B 0x03 0x04`)

---

## 3. API Surface & DTO Conventions

- **Success Responses**: Endpoints with no content body return `OkResponse { Ok: true }` (`{"ok":true}`). Do not return anonymous objects.
- **Decimal Serialization**: Currencies, totals, margins, and rates must be serialized as strings with fixed decimal places via DTO converters in `src/BidParser.Api/Serialization/`:
  - `fx_rate`: 4 decimal places (e.g. `"0.7400"`)
  - `margin`: 2 decimal places (e.g. `"7.50"`)
  - `computed_total`, `quoted_total`: 2 decimal places (e.g. `"1625358.51"`)
- **JSON Casing**: `JsonNamingPolicy.SnakeCaseLower` is applied globally. Avoid `[JsonPropertyName]` attributes on new DTOs.
- **Error Formats**: Branch based on failure kind using typed records:
  - `ApiError { Detail }` -> `{"detail": "..."}` for standard errors (400, 401, 403, 404, 409, 429).
  - `PasswordValidationError { Detail }` -> `{"detail": ["msg1", "msg2"]}` for password changes (each entry is a rule violation).
  - `ParseErrorResponse { Detail: { Stage, Hint, Message } }` -> `{"detail": {...}}` for `/parse` 422 parser failures.
