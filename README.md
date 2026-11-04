# Velavan Demo

Phase 1, Step 6 administrator user management for the Velavan Demo application.

This step adds role-filtered user listing, profile editing, role and status management, a change-password dialog, admin-only navigation, and the supporting API updates on top of the Step 5 authentication frontend. It does not change the database schema.

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
- `VelavanDemo.UnitTests` → `VelavanDemo.Domain`, `VelavanDemo.Application`
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
dotnet ef database update --project src/VelavanDemo.Infrastructure --startup-project src/VelavanDemo.Api
dotnet run --project src/VelavanDemo.Api --launch-profile https
```

Swagger is available in Development at `https://localhost:7161/swagger`. Use the **Authorize** button with a Bearer access token from `/api/auth/login`.

The development connection string in `src/VelavanDemo.Api/appsettings.Development.json` targets the `VelavanDemo` database on the default SQL Server instance using Windows Authentication. Migrations are in `src/VelavanDemo.Infrastructure/Persistence/Migrations`.

The `AddUsernameToUsers` migration adds required normalized usernames, backfills existing usernames from the current email values, and makes email optional. Review the generated migration before applying it. Applying the migration is a separate, explicit operation and is not performed automatically by the API.

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

### Tests

```powershell
dotnet test VelavanDemo.sln
```

The integration suite uses `WebApplicationFactory<Program>` with an isolated EF Core InMemory database and never connects to the configured SQL Server database. It covers username login success/failure, case-insensitive usernames, optional email, inactive accounts, JWT access and claims, authorization policies, admin privilege restrictions, user list search/status/role filters and pagination, profile creation and editing, duplicate usernames, duplicate emails, weak passwords, role updates, status activation/deactivation, last-active-Super-Admin protection, password changes, deactivation invalidating an existing token, and expired tokens.

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

This step does not include:

- Refresh tokens, token revocation lists, or server-side sessions
- User deletion, self-registration, password reset, email confirmation, MFA, or login rate limiting
- Audit logging implementation or user-change history screens
- Inventory, ERP, orders, delivery, reporting, or other business endpoints
- A theme redesign or additional branding API integration
- Deployment setup
- Git initialization or commits
