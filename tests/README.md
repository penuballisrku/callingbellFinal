# Calling Bell tests

One command runs everything: database check, unit tests, web build, API checks, phone checks, browser checks, then cleanup.

```powershell
# API (http://localhost:5080) and web app (http://localhost:5173) running first, see CLAUDE.md "Running locally"
.\tests\RunAllTests.ps1 -Phone 98XXXXXXXX
```

| Switch | Use |
|---|---|
| `-Phone 98XXXXXXXX` | Runs the phone checks for that number. You can set `CB_TEST_PHONE` instead. The number is never stored in the repo. |
| `-Interactive` | Asks you to type the OTP you received, for when real SMS or WhatsApp is configured and the API doesn't return the code. |
| `-Api`, `-Web` | Point at other ports, e.g. `-Api http://localhost:5081 -Web http://localhost:5174`. |
| `-ApplyDatabase` | Runs `database\scripts\RunAll.sql` first. |
| `-SkipUnit`, `-SkipBuild`, `-SkipE2E`, `-SkipCleanup` | Leave a step out. |

The runner exits with 0 when every step passes and 1 otherwise.

## Scripts

| File | What it checks |
|---|---|
| `api/phase2_api.py` | **Chat:** access rules, line breaks, unread counts, read receipts, signed private attachments, file-type rules. **Team:** owner and public views, validation, per-person slots. **Video:** room access, ICE servers, ending a call. |
| `api/phone_flow.py --phone …` | Finds the account that owns the number. Makes sure a SIGNUP request doesn't reveal that the number is registered. Requests a LOGIN code and prints the delivery chain (WhatsApp → SMS). Rejects a wrong code and a reused one. Then signs in, books an online consultation that the owner confirms, and prints how the BOOKING and VIDEO notifications were routed to the number. |
| `e2e/phase2.cjs` | Two signed-in browsers: live chat with typing and read ticks, adding a team member, the bookings Professional column, choosing a professional when booking, a two-way WebRTC video call (fake camera), and the mobile layout. Screenshots go to `e2e/output/`. |
| `sql/CleanupTestData.sql` | Removes everything the tests created. Test data carries the marker `[cb-test]`; seeded and real data is left alone. |
| `lib/cbtest.py` | Shared helpers. Uses only the Python standard library and waits out the `auth` rate limit (10 sign-ins a minute per IP). |

Requirements:
- Python 3.
- Node 20+ (run `npm install` in `tests`; the runner does it if needed).
- The installed Chrome (set `CB_CHROME` if it isn't at the default path).
- `sqlcmd`.

Limits to remember when re-running the phone checks: 3 codes per number every 15 minutes (5 an hour), 30 seconds apart, and 10 per IP every 15 minutes. When a limit is hit, the code step is reported as SKIP, not FAIL.

## Receiving the messages on a real phone

In development the OTP goes to the **Log** SMS provider. It writes a masked line to the API log and the API returns the code, so nothing reaches the phone. To receive real messages, configure a provider with user-secrets (never in appsettings), restart the API, and run with `-Interactive`:

```powershell
cd backend\src\CallingBell.Api
# SMS through MSG91: needs a DLT-registered sender, entity and templates
dotnet user-secrets set "Notifications:Sms:Msg91:Enabled" "true"
dotnet user-secrets set "Notifications:Sms:Msg91:AuthKey" "<msg91 auth key>"
dotnet user-secrets set "Notifications:Sms:Msg91:SenderId" "<6-letter sender>"
dotnet user-secrets set "Notifications:Sms:Msg91:DltEntityId" "<DLT entity id>"
# WhatsApp Cloud API: with Meta's test number, first add your phone as a recipient in the Meta app dashboard
dotnet user-secrets set "Notifications:WhatsApp:Enabled" "true"
dotnet user-secrets set "Notifications:WhatsApp:PhoneNumberId" "<phone number id>"
dotnet user-secrets set "Notifications:WhatsApp:AccessToken" "<access token>"
```

- **MSG91 template IDs:** put each approved DLT template ID in `dbo.NotificationTemplates.TemplateName` for the `SMS` rows, which currently hold placeholders like `DLT_TEMPLATE_ID_OTP`.
- **WhatsApp templates:** the template names there (`callingbell_otp`, booking, video) must be approved in your WhatsApp Business account.
- **Local log provider:** to stop the development log provider from taking the code before a real gateway, set `Notifications:Sms:Log:Enabled` to `false` in `appsettings.Development.json`. It runs last (priority 9), so a configured gateway is tried first anyway.
