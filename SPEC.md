# Island specification

Build a "dynamic island" for Windows 10/11: a small black pill at the top center of the screen that
shows live info at a glance and expands into a panel with revenue, calendar and email, so the user
doesn't need to open a browser.

The macOS/iPhone Dynamic Island is the visual inspiration only. Do NOT use Apple icons, fonts, images
or the "Dynamic Island" name in the app. The app is called "Island".

## Tech stack
- C# with .NET 8 and WPF. Project location: winmac\Island
- The dock project in winmac\MacDock is paused. You may copy useful code from it (transparent
  topmost window, WS_EX_TOOLWINDOW / WS_EX_NOACTIVATE, DPI handling, fullscreen detection, tray icon),
  but do not modify it.
- Allowed NuGet packages: Google.Apis.Gmail.v1, Google.Apis.Calendar.v3, Google.Apis.Auth,
  System.Security.Cryptography.ProtectedData. Use plain HttpClient for Stripe and Polar.
- No admin rights required.
- Run `dotnet build` after every step and fix all errors and warnings before moving on.

## Look and animation
- Collapsed: a solid black pill (about 200 x 36 px) centered at the top of the primary monitor, a few
  pixels below the top edge. Fully rounded ends.
- The collapsed pill shows one "live activity" at a time on its left and right sides, e.g.
  calendar icon + "Standup in 12m", or "$1,240 today", or a mail icon + unread count.
  It rotates between them every ~8 seconds, with a smooth crossfade.
- Hover (after ~150 ms) or click expands it into a large rounded black panel (about 640 x 320 px)
  with three cards: Revenue, Calendar, Email. Moving the mouse away collapses it after ~400 ms.
- Expand/collapse uses a spring animation (slight overshoot, then settle) on width, height and corner
  radius, at the display's refresh rate. The content fades in after the shape has mostly expanded.
  The render loop stops when nothing is animating, so CPU is near 0% while idle.
- Alerts: when something important happens (new sale, meeting starting in 5 minutes, new important
  email), the pill briefly grows into a wider "alert" shape for ~5 seconds showing the event, then
  shrinks back. Queue alerts so they never overlap.
- White text, a clean system font (Segoe UI Variable), small colored accents (green for revenue,
  blue for calendar, red badge for unread mail).
- Transparent areas around the pill must let clicks pass through. Never steal focus, never appear in
  Alt+Tab or the taskbar, always on top. Hide when a fullscreen app or game runs on that monitor.
- Support per-monitor DPI scaling.

## Revenue (Stripe and Polar first, more providers later)
- Define an `IRevenueProvider` interface (name, connect/test, fetch summary, fetch recent sales) so
  more providers (Gumroad, Lemon Squeezy, Shopify, Paddle) can be added later by pasting an API key.
- Stripe: the user pastes a restricted key with read-only access. Use the REST API
  (https://api.stripe.com/v1), e.g. balance transactions or charges. Amounts are in the smallest
  currency unit (cents); convert correctly, including zero-decimal currencies like JPY.
- Polar: the user pastes an organization access token. Use the REST API (https://api.polar.sh/v1),
  e.g. orders and metrics endpoints.
- Show: revenue today, this month, last 30 days, and the 5 most recent sales (amount, product/customer
  if available, time ago). Combine providers, and show a per-provider breakdown on hover.
- "Today" means the user's local timezone. If sales are in multiple currencies, show each currency
  separately rather than adding them together.
- Refresh every 5 minutes, plus a manual refresh button. A new sale since the last refresh triggers
  an alert ("+$49.00 via Stripe").

## Google Calendar and Gmail
- Sign in with Google using OAuth for desktop apps (loopback redirect + PKCE, opens the browser
  once). Scopes: calendar.readonly and gmail.readonly only.
- The user must create their own Google Cloud project and OAuth client ID. The README must explain
  this step by step for a beginner (enable the Gmail and Calendar APIs, OAuth consent screen in
  "Testing" mode with their own email as a test user, create a "Desktop app" client, paste the
  client ID and secret into Island's settings).
- Calendar: show the next 5 events today/tomorrow with time and title, and a countdown for the next
  one. Clicking an event opens it in the browser; if it has a Google Meet/Zoom link, show a "Join"
  button. Alert 5 minutes before an event starts. Refresh every 5 minutes.
- Gmail: show the unread count in the inbox and the 5 most recent unread emails (sender, subject,
  time). Clicking one opens it in Gmail in the browser. Alert for new emails Gmail marks as
  IMPORTANT. Refresh every 2 minutes (use the history API to avoid refetching everything).
- Never download or store full email bodies.

## Settings and security
- System tray icon with: Settings, Refresh now, Pause alerts, "Start with Windows" (HKCU Run key),
  and Exit.
- Settings window: connect/disconnect Google, paste Stripe and Polar keys with a "Test connection"
  button, refresh intervals, enable/disable each card, and alert toggles.
- Store all secrets (API keys, Google refresh token) encrypted with Windows DPAPI (ProtectedData,
  CurrentUser scope) in %AppData%\Island\secrets.dat. Store normal settings as JSON in
  %AppData%\Island\settings.json. Never write secrets to logs or plain text files.
- Handle errors gracefully: a bad key, no internet, an expired token or a rate limit shows a small
  error state on that card ("Stripe: invalid key - open Settings"), never a crash. Back off on
  repeated failures.
- Allow only one running instance.

## Milestones (build and test in this order)
1. Collapsed pill at the top center with the expand/collapse spring animation, placeholder cards,
   tray icon, click-through, fullscreen hiding.
2. Settings window and encrypted secret storage.
3. Revenue: provider interface, Stripe and Polar, revenue card, sale alerts.
4. Google sign-in, Calendar card, meeting alerts.
5. Gmail card and important-email alerts.
6. Live activities rotating in the collapsed pill, alert queue polish, start with Windows.

## Quality
- Clear file structure, e.g. IslandWindow, IslandAnimator, Cards/, Providers/ (StripeProvider,
  PolarProvider), Google/ (GoogleAuth, CalendarService, GmailService), SecretStore, Settings,
  NativeMethods.
- Keep memory under about 150 MB and CPU near 0% when idle.

## Deliverables
- A working project that runs with `dotnet run`.
- A beginner-friendly README: installing the .NET 8 SDK, running the app, setting up the Google
  Cloud OAuth client, creating a read-only Stripe restricted key, creating a Polar access token,
  publishing a single-file .exe, and starting with Windows.
- After each milestone, explain in plain language what changed and what to test with `dotnet run`.
