# Velavan Demo

Phase 2 attachment support for the Velavan Demo application.

This phase adds product images, customer documents, and order attachments with local filesystem storage, upload validation, malware-scanning hooks, storage reconciliation, admin-only APIs, reusable Angular attachment components, and the `AddAttachmentModule` plus `AddAttachmentIntegrityConstraints` EF Core migrations. It builds on the Phase 1 authentication and administrator user-management features. Both migrations are generated but not applied to any database as part of this work.

## Solution Structure

```text
VELAVAN/
├── VelavanDemo.sln
├── src/
│   ├── VelavanDemo.Domain/
│   ├── VelavanDemo.Application/
│   ├── VelavanDemo.Infrastructure/
│   └── VelavanDemo.Api/
├── tests/
│   ├── VelavanDemo.UnitTests/
│   └── VelavanDemo.IntegrationTests/
├── frontend/
│   └── velavan-demo-web/
├── docs/
│   └── ui-reference/
├── scripts/
├── .gitignore
└── README.md
```

Project references:

- `VelavanDemo.Application` → `VelavanDemo.Domain`
- `VelavanDemo.Infrastructure` → `VelavanDemo.Application`, `VelavanDemo.Domain`
- `VelavanDemo.Api` → `VelavanDemo.Application`, `VelavanDemo.Infrastructure`
- `VelavanDemo.UnitTests` → `VelavanDemo.Domain`, `VelavanDemo.Application`, `VelavanDemo.Infrastructure`
- `VelavanDemo.IntegrationTests` → `VelavanDemo.Api`, `VelavanDemo.Infrastructure`, `VelavanDemo.Application`, `VelavanDemo.Domain`

## Prerequisites

- .NET SDK 9 (used to build the `net8.0` projects)
- .NET 8 runtime for running the API
- Node.js and npm
- SQL Server Developer Edition with the default `MSSQLSERVER` instance and Windows Authentication
- Angular CLI 19.2.25 is installed locally in the frontend project

## Backend

```powershell
dotnet restore VelavanDemo.sln
dotnet build VelavanDemo.sln
dotnet run --project src/VelavanDemo.Api --launch-profile https
```

Apply migrations only as a separate, explicitly approved operation:

```powershell
dotnet ef database update --project src/VelavanDemo.Infrastructure --startup-project src/VelavanDemo.Api
```

Swagger is available in Development at `https://localhost:7161/swagger`. Use the **Authorize** button with a Bearer access token from `/api/auth/login`.

The development connection string in `src/VelavanDemo.Api/appsettings.Development.json` targets the `VelavanDemo` database on the default SQL Server instance using Windows Authentication. Migrations are in `src/VelavanDemo.Infrastructure/Persistence/Migrations`.

The `AddUsernameToUsers` migration adds required normalized usernames, backfills existing usernames from the current email values, and makes email optional.

The `AddAttachmentModule` migration creates `Attachments`, `ProductImages`, `CustomerDocuments`, and `OrderAttachments`, including the filtered unique primary-image index. The `AddAttachmentIntegrityConstraints` migration adds check constraints for positive attachment sizes, valid image dimensions, and non-negative product-image sort order. Both migrations were generated but have not been applied. Review them before applying. Applying migrations is a separate, explicit operation and is not performed automatically by the API.

Production startup validates attachment storage and requires a real malware scanner. The API will fail fast if `Attachments:Storage:Provider` is unsupported, the storage root is invalid, or `Attachments:Scanner:Provider` remains `None` outside Development/Testing. Outside Development and Testing, scanner configuration must use an absolute HTTPS endpoint and a non-empty `Attachments:Scanner:ApiKey`; startup fails when the endpoint uses HTTP or credentials are missing. The local attachment storage root is created with restrictive ACLs on Windows, restricted Unix permissions elsewhere, and the API performs a startup write/read/delete probe. For production, configure the scanner and optional storage service identity through environment variables or a secret store, for example:

```powershell
Attachments__Scanner__Provider=Http
Attachments__Scanner__Endpoint=https://scanner.example.test/scan
Attachments__Scanner__ApiKey=<secret>
Attachments__Storage__ServiceAccount=<optional-api-service-account>
Attachments__Reconciliation__OrphanGracePeriodMinutes=30
```

### Authentication Configuration

JWT issuer, audience, and access-token lifetime are configured in `appsettings.json`. The signing secret and Super Admin seed values must be supplied through user-secrets or environment variables and must never be committed.

The API project already has a `UserSecretsId`. For a fresh environment:

```powershell
dotnet user-secrets set "Jwt:Secret" "<at-least-32-byte-random-secret>" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:Enabled" "true" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:Username" "<super-admin-username>" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:Email" "<optional-contact-email>" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:FirstName" "Super" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:LastName" "Admin" --project src/VelavanDemo.Api
dotnet user-secrets set "Seed:SuperAdmin:Password" "<strong-random-password>" --project src/VelavanDemo.Api
```

`Seed:SuperAdmin:Username` is required when seeding is enabled. `Seed:SuperAdmin:Email` is optional contact information and is no longer used for sign-in. User secrets are loaded only in the Development environment and are captured when the process starts, so restart the API after changing them.

Retrieve the locally generated initial Super Admin credentials with:

```powershell
dotnet user-secrets list --project src/VelavanDemo.Api
```

Do not share the output or commit it. The startup seeder is idempotent: it creates the `Super Admin`, `Admin`, and `User` roles when missing, creates the initial Super Admin only when no Super Admin account exists, and updates a legacy email-derived Super Admin username once after the username migration. It does not reset passwords, remove roles, change account status, or delete data.

For non-development environments, use environment variables such as `Jwt__Secret`, `Seed__SuperAdmin__Enabled`, `Seed__SuperAdmin__Username`, and `Seed__SuperAdmin__Password` instead of user-secrets.

### Authentication Endpoints

| Method | Route | Access | Purpose |
| --- | --- | --- | --- |
| `POST` | `/api/auth/login` | Anonymous | Validate a username and password and return a JWT plus user details |
| `GET` | `/api/auth/me` | Authenticated | Return the current active user |
| `POST` | `/api/auth/change-password` | Authenticated | Verify the current password and set a new strong password |
| `GET` | `/api/admin/users` | Admin or Super Admin | List/filter/paginate users |
| `GET` | `/api/admin/users/{id}` | Admin or Super Admin | Get a user |
| `POST` | `/api/admin/users` | Admin or Super Admin | Create a standard user; Super Admin may assign roles |
| `PUT` | `/api/admin/users/{id}` | Admin or Super Admin | Update permitted profile details |
| `PUT` | `/api/admin/users/{id}/roles` | Super Admin | Replace a user's roles |
| `PATCH` | `/api/admin/users/{id}/status` | Admin or Super Admin | Activate, deactivate, or suspend a user |

Authenticated requests use:

```http
Authorization: Bearer <access-token>
```

JWT claims use `sub`, `username`, `email` (when an optional email is present), `name`, and `role`. Deactivated users are rejected on login and on every authenticated request that uses an existing token. Email values are optional contact information and are not used for authentication.

### Authorization Rules

- `Super Admin`: full user administration and role assignment.
- `Admin`: can view and manage standard `User` accounts only; cannot assign privileged roles or edit/change the status of privileged accounts.
- `User`: no access to administrator endpoints.
- Administrators cannot change their own status or remove their own Super Admin role.
- At least one active Super Admin must remain.
- User list filters support search, status, role, and server-side pagination.
- Unknown roles, duplicate usernames, duplicate non-null emails, and invalid passwords return explicit `400`, `401`, `403`, or `409` responses.
- Password hashes are never returned by user-management endpoints.

### Attachments

Attachment endpoints require the `Admin or Super Admin` policy.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/admin/products/{productId}/images` | List product images |
| `POST` | `/api/admin/products/{productId}/images` | Upload a product image (`multipart/form-data`, field `file`) |
| `PATCH` | `/api/admin/products/{productId}/images/{attachmentId}/primary` | Set the primary image |
| `PUT` | `/api/admin/products/{productId}/images/order` | Reorder images |
| `DELETE` | `/api/admin/products/{productId}/images/{attachmentId}` | Hard-delete an image and its stored file |
| `GET` | `/api/admin/customers/{customerId}/documents` | List customer documents |
| `POST` | `/api/admin/customers/{customerId}/documents` | Upload a customer document |
| `DELETE` | `/api/admin/customers/{customerId}/documents/{attachmentId}` | Hard-delete a customer document |
| `GET` | `/api/admin/orders/{orderId}/attachments` | List order attachments |
| `POST` | `/api/admin/orders/{orderId}/attachments` | Upload an order attachment |
| `DELETE` | `/api/admin/orders/{orderId}/attachments/{attachmentId}` | Hard-delete an order attachment |
| `GET` | `/api/attachments/{attachmentId}` | Get attachment metadata |
| `GET` | `/api/attachments/{attachmentId}/content` | Download or preview content; supports `download=true`, ETag/304, and range requests |

Limits and validation:

- Product images: JPEG, PNG, or WebP, up to 10 MB each, maximum 8 per product.
- Customer documents and order attachments: PDF, JPEG, PNG, or WebP, up to 20 MB each, maximum 25 per owner.
- File signatures and declared content types are checked against the extension; SVG and executable content are rejected. Empty and `application/octet-stream` declared types are accepted only after signature validation.
- File names are sanitized, SHA-256 is recorded, and image dimensions are bounded.
- Inline preview is limited to JPEG, PNG, and WebP; PDFs are download-only.
- Deletes are hard deletes: the join record and attachment metadata are removed in a transaction, then the stored file is deleted. A failed physical delete is logged and retried by reconciliation.
- Uploads are rate-limited to 30 requests per minute per user/IP.
- Upload request limit is 26 MB to accommodate multipart overhead.
- Attachment mutations are serialized to prevent count, quota, primary-image, and sort-order races. Database unique conflicts return `409`.
- Quota, scanner, and storage failures return `507`, `422`/`503`, and `503`/`500` responses as appropriate.

Storage, scanner, and reconciliation configuration is in `src/VelavanDemo.Api/appsettings.json`:

```json
"Attachments": {
  "Storage": {
    "Provider": "Local",
    "RootPath": "C:\\ProgramData\\VelavanDemo\\attachments",
    "MaxTotalBytes": 0,
    "ServiceAccount": ""
  },
  "Scanner": {
    "Provider": "None",
    "Endpoint": "",
    "ApiKey": "",
    "TimeoutSeconds": 30
  },
  "Reconciliation": {
    "Enabled": false,
    "RunOnStartup": false,
    "IntervalMinutes": 1440,
    "OrphanGracePeriodMinutes": 30
  }
}
```

`MaxTotalBytes` is a global storage quota; `0` disables the quota. The local provider writes outside the repository and application content root (including `wwwroot`), rejects filesystem roots, symbolic links, junctions, alternate data streams, and traversal keys, and cleans temporary upload files. On Windows it applies restrictive ACLs that grant only the configured API service identity plus necessary system administrators and the current process, removes inherited broad identities, and performs a startup write/read/delete probe. On non-Windows platforms it applies owner-only Unix permissions. Ensure the API process can create and write to this directory.

`NoOpUploadedFileScanner` is available only in Development/Testing and performs no malware scanning. Outside those environments, set `Attachments:Scanner:Provider` to `Http`, set an absolute HTTPS `Attachments:Scanner:Endpoint`, and set a non-empty `Attachments:Scanner:ApiKey`; startup fails when the endpoint uses HTTP or credentials are missing. The HTTP scanner posts the file as multipart field `file`, sends the bearer API key when configured, treats `2xx` as clean, `400`/`422` as malware rejection, and other outcomes/timeouts as scanner unavailable.

Reconciliation is disabled by default. When enabled, it deletes stored files with no attachment metadata after the configurable `Attachments:Reconciliation:OrphanGracePeriodMinutes` period and logs metadata whose stored file is missing. Reconciliation uses the attachment mutation concurrency gate and skips recently written files within the grace period.

### Tests

```powershell
dotnet test VelavanDemo.sln
```

The integration suite uses `WebApplicationFactory<Program>` with an isolated EF Core InMemory database and a temporary attachment storage root; it never connects to the configured SQL Server database or the production storage path. It covers username login success/failure, case-insensitive usernames, optional email, inactive accounts, JWT access and claims, authorization policies, admin privilege restrictions, user list search/status/role filters and pagination, profile creation and editing, duplicate usernames, duplicate emails, weak passwords, role updates, status activation/deactivation, last-active-Super-Admin protection, password changes, deactivation invalidating an existing token, expired tokens, attachment upload/list/download/delete flows, type and signature validation, size and count limits, ownership checks, primary-image and reorder rules, ETag/304, missing physical files, storage-write failures, scanner rejection/unavailability, storage quota enforcement, concurrent upload limits, and production startup scanner validation. Unit tests cover local storage path/inventory safeguards, cancellation cleanup, physical-delete failures, unique-conflict cleanup, and reconciliation.

## Frontend

```powershell
cd frontend/velavan-demo-web
npm install
npm start
```

The Angular development server runs at `http://localhost:4200`.

The development API base URL is `http://localhost:5227/api` in `src/environments/environment.development.ts` and must match a configured API CORS origin. The API HTTP profile exposes this port; the HTTPS profile binds both `https://localhost:7161` and `http://localhost:5227`.

Production build:

```powershell
npm run build
```

Frontend tests:

```powershell
npm test -- --watch=false --browsers=ChromeHeadless
```

### Frontend Authentication

Routes:

| Route | Access | Purpose |
| --- | --- | --- |
| `/login` | Anonymous | Sign in with username and password |
| `/` | Authenticated | Protected admin shell and dashboard |
| `/users` | Admin or Super Admin | User list, create/edit, roles, and status management |
| `/forbidden` | Authenticated | Access-denied page for role-protected routes |

The frontend authentication flow:

- `AuthService` signs in through `POST /api/auth/login`, stores the session in `sessionStorage`, and exposes the current user, roles, and display state through Angular signals.
- On startup, a valid stored session is restored and revalidated through `GET /api/auth/me`.
- `authInterceptor` adds `Authorization: Bearer <token>` only to configured API requests. Login and third-party requests are excluded.
- Protected API `401` responses and token expiry clear the session and return the user to `/login`. Deactivated accounts are rejected by the API and follow the same path.
- `authGuard` protects the admin shell, `guestGuard` prevents authenticated users from reopening the login page, and `roleGuard` protects routes that declare required roles in route `data.roles`.
- The dashboard is available to every active authenticated role. The `/users` route and sidebar item are limited to Admin and Super Admin.
- Login errors distinguish invalid credentials, inactive/suspended accounts, validation failures, and network failures.
- The header displays the signed-in user, primary role, page title, change-password action, and logout action.
- Unsafe external `returnUrl` values are rejected.

No additional npm package is required. Tokens are stored in JavaScript-readable `sessionStorage`, so XSS remains a risk; the API remains the authorization authority.

### Frontend User Management

- The users table shows name, username, roles, status, and created date with server-side search, role/status filters, and pagination. Email remains available as optional contact information in the create/edit forms.
- Admin users can create standard users and edit standard-user profile details. Super Admins can assign any role and manage privileged accounts.
- Row actions respect the backend rules: Admin users cannot edit or change the status of privileged accounts, and no user can change their own status.
- Create, edit, role, and status actions surface backend validation, duplicate-username, duplicate-email, permission, and last-Super-Admin errors.
- Status changes are confirmed before saving; deactivation immediately blocks the account from authenticated requests.
- The header change-password dialog verifies the current password and enforces the same strong-password policy as the API. The current session remains signed in.
- Role changes are saved immediately but the user's existing JWT retains its previous role claims until the token expires or the user signs in again. This is shown in the role dialog.

### Frontend Attachments

Reusable components live under `frontend/velavan-demo-web/src/app/features/attachments/`:

- `attachment-manager` coordinates upload, listing, deletion, progress, and error feedback for an owner.
- `attachment-upload` and `attachment-list` provide the upload control and file rows.
- `image-gallery` and `product-image-manager` provide image ordering, primary-image selection, and preview.
- `attachment-thumbnail` and `attachment-preview-dialog` render authenticated image previews and download links.

Shared HTTP, model, and policy helpers live in `frontend/velavan-demo-web/src/app/core/attachments/`. The components are reusable building blocks; no standalone attachment route or demo page was added. They require an Admin or Super Admin session and use the authenticated attachment endpoints above. Client-side limits come from `attachment-policy.ts`, stale list responses are ignored, concurrent row actions are tracked per attachment, Blob ProblemDetails errors are parsed for user feedback, and object URLs are revoked safely after preview/download use.

The development workflow is:

```powershell
dotnet run --project src/VelavanDemo.Api --launch-profile https
cd frontend/velavan-demo-web
npm start
```

Use a locally seeded account from user-secrets to sign in. Do not commit or share those credentials.

## Branding

Branding defaults live in one place:

`frontend/velavan-demo-web/src/app/core/branding/branding.config.ts`

`BrandingService` exposes the branding through signals. Components do not hardcode the project name. The service is ready to receive API-provided branding in a later step.

The API also has centralized branding settings in `appsettings.json` under `Branding`.

## UI Reference

The reference folder is:

`docs/ui-reference/QuickCart_UI_UX_Separate_Screens/`

It contains 17 PNG reference screens and a README. No ZIP file is present in the workspace, but the extracted reference images are available and have been inspected. They are preserved unchanged. The authentication and user-management screens use the existing application theme; broader UI implementation is deferred to later approved steps.

## Scope Notice

This phase does not include:

- Refresh tokens, token revocation lists, or server-side sessions
- User deletion, self-registration, password reset, email confirmation, MFA, or login rate limiting
- Audit logging implementation or user-change history screens
- Inventory, ERP, order lifecycle, delivery, reporting, or other business endpoints
- Production malware scanning, cloud/blob storage, or CDN delivery
- A theme redesign or additional branding API integration
- Applying the `AddAttachmentModule` migration to a database
- Deployment setup
- Git initialization or commits
