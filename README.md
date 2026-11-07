# Island

A small Windows 10/11 tray app for revenue, your Google calendar, and unread inbox mail. Built with C#/.NET 8 and WPF, with no administrator installation. MacDock is a separate, untouched project.

## Install and run

1. Download the **.NET 8 SDK for Windows x64** from [Microsoft](https://dotnet.microsoft.com/download/dotnet/8.0). Choose the SDK, not just the runtime. Restart your terminal after installing.
2. Open PowerShell in this folder (`winmac\Island`).
3. Run `dotnet build`, then `dotnet run`.
4. Hover over the black pill at the top of your primary display. Right-click the Island tray icon (possibly under the hidden-icons arrow) and choose **Settings**.

If PowerShell cannot find dotnet, use `& 'C:\Program Files\dotnet\dotnet.exe' run` (or `build`). Only one Island instance can run in your Windows session. Exit from the tray before rebuilding if Windows reports that the executable is locked.

When the island is expanded, click **Apps** at the top left to browse its built-in apps by name and icon. Click an app to open it inside the island, or use the dock to switch apps directly.

Running countdowns, Pomodoro sessions, and stopwatches automatically appear in the closed notch with their app icon and update every second. Collapsing the Claude page shows today's token usage; running timers take priority. In Message, **Show on notch** and **Flash it** close the island to display your animated message there. An enabled message takes priority over task text until switched off; notifications can temporarily interrupt it.

The overlay does not activate or take keyboard focus. Settings is a normal focusable window so you can type credentials. Hover for 150 ms to expand; leaving for 400 ms collapses. Scroll within a card for longer content. Event and email links open your browser intentionally. The overlay hides while the foreground window covers the primary monitor. Maximized borderless applications may also be treated as fullscreen. It does not reserve desktop space or move other windows.

## Connect your Google account

You supply your own Google project; Island has no shared credentials or backend.

1. Visit [Google Cloud Console](https://console.cloud.google.com/) and sign in with the Google account you want to use.
2. Use the project selector at the top and choose **New Project**. Name it `Island personal` and create it. Select that project.
3. Open **APIs & Services → Library**. Search for **Google Calendar API**, open it, and click **Enable**. Repeat for **Gmail API**.
4. Open **Google Auth Platform** (or **APIs & Services → OAuth consent screen**). Start configuration if prompted. Supply an app name (`Island`), your support email, and a developer contact email.
5. Select an **External** audience for a personal Google account. Keep publishing status **Testing**. Under **Audience → Test users**, add the exact Google email address you will sign in with. Save it.
6. Under **Data Access**, add only these scopes: `https://www.googleapis.com/auth/calendar.readonly` and `https://www.googleapis.com/auth/gmail.readonly`. Save. Island never requests permission to modify mail or calendars.
7. Under **Clients** (or **Credentials → Create credentials → OAuth client ID**), choose **Desktop app**, name it `Island desktop`, and create it. Do not choose Web application; the desktop client supports the local callback port used by Island.
8. Copy the **Client ID** and **Client secret** into Island Settings. Click **Connect Google**. This saves those credentials encrypted and opens your default browser.
9. Select your test-user account and grant the two read-only permissions. Because your app is in testing, Google may show an unverified-app notice. Continue only for the project you created. Return to Island when the browser says to do so. Sign-in times out after three minutes if abandoned.
10. Click **Save** and **Refresh now** from the tray. You should see your primary calendar's next five events today/tomorrow and your inbox unread messages.

Google testing-mode refresh tokens can expire after seven days; reconnect if Island reports an expired/invalid token. Organization accounts may need their administrator to allow the app. Disconnect removes the local refresh token. To revoke the grant at Google as well, remove Island under [Google account connections](https://myaccount.google.com/connections). Google may change console labels; see its [desktop OAuth guide](https://developers.google.com/identity/protocols/oauth2/native-app).

Calendar uses your Windows local timezone for the today/tomorrow window. Timed events have links and, when supplied by Google or found in an event's location/description, Meet/Zoom Join links. All-day events have no countdown or starting-soon alert. Meeting checks occur every 30 seconds; alerts are approximate, and a suspended or offline PC can miss one.

Gmail requests only metadata (From and Subject headers, labels, timestamp, and IDs), never message bodies or attachments. It keeps the current five headers and history cursor in memory. History drives subsequent updates; an expired cursor causes a fresh synchronization. See [Google's synchronization guide](https://developers.google.com/workspace/gmail/api/guides/sync).

## Connect Stripe

1. Sign in to your [Stripe Dashboard](https://dashboard.stripe.com/). Select the intended account and live or test environment.
2. Find **Developers → API keys** (it may be under Workbench). Choose **Create restricted key**. Name it `Island read only`.
3. Grant **Read** access to **Charges**, leaving all write permissions and unrelated permissions disabled. Island reads `/v1/charges`; it does not create payments or change your account.
4. Copy the restricted key into Island's Stripe field. Click **Test Stripe** and then **Save**. Keep this key private. Do not use an unrestricted secret key.
5. Choose **Refresh now**. Keys for test mode show test sales; use a live restricted key for live sales.

Stripe totals use successful paid charges minus refunded amounts. This is collected sales revenue including tax where charged, before processing fees; it is not a balance, accounting profit, or payout report. Partial refunds reduce the original sale's period. Currency conversion follows [Stripe's currency rules](https://docs.stripe.com/currencies), including zero-decimal JPY and charge-specific ISK/HUF/TWD behavior.

## Connect Polar

1. Sign in at [Polar](https://polar.sh/) and select your organization.
2. Open organization **Settings → Developers** and find **Organization Access Tokens**. Create a token named `Island`.
3. Select **orders:read** only, copy the token, and paste it into Island's Polar field.
4. Click **Test Polar**, **Save**, then **Refresh now**. If testing fails, check the token's organization and scope.

Island reads paginated paid orders from [Polar's orders API](https://polar.sh/docs/api-reference/orders/list), using total amount less refunded amount. It does not write to Polar. To disconnect a revenue provider, clear its key and save. Revoke unused keys/tokens in the provider dashboard.

## Totals, polling, and alerts

Today starts at local midnight; Month starts on the local first day; 30 days is a rolling period. Every currency has its own total, so USD and EUR are never added. Hover over revenue text for today's per-provider breakdown. Recent sales show the newest five across providers.

Revenue and Calendar default to five-minute refreshes; Gmail defaults to two minutes. Set 1–60 minutes per card in Settings. Manual **Refresh now** bypasses the schedule. Failures back off exponentially to a maximum of 60 minutes. Each provider is independent. Existing data may remain visible when a refresh fails. Sale alerts compare successful refreshes within this running session; the initial load is a baseline and does not alert for old sales. Relaunching resets that baseline. Unread and important are separate Gmail labels; important-mail alerts use Gmail's IMPORTANT classification.

Alerts queue sequentially and display for five seconds each. Pause alerts suppresses new alerts and clears the pending queue. Disable cards or individual alert types in Settings. The tray Refresh now and Settings commands remain available when the pill is hidden. No service runs when Island exits.

## Security and files

`%AppData%\Island\settings.json` holds ordinary preferences. `%AppData%\Island\secrets.dat` contains DPAPI CurrentUser-encrypted keys, OAuth client credentials, refresh token, and account identifier. The access token stays in memory. Credentials are never logged. The encrypted file is tied to your Windows account and machine; copying it to another account is not a supported backup. Reset credentials by exiting Island and deleting `secrets.dat`, then reconnecting. No full emails or persistent sales/event cache is stored.

The app uses framework HttpClient for all HTTPS APIs and a loopback-only listener during Google sign-in. No NuGet dependencies are required: WPF's Windows desktop framework provides ProtectedData. Account setup requires internet access.

## Publish an executable

From this folder run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Run `publish\Island.exe`. The self-contained executable includes .NET and is larger than a framework-dependent build. Native runtime components extract on first launch. Use `win-arm64` for an ARM Windows machine. No administrator rights are needed. Publishing a new runtime may need internet to download its runtime pack.

## Start with Windows

Put the published executable in a permanent folder, run it there, and choose **Start with Windows** in the tray. Island sets only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Island`. Uncheck the menu item before moving/deleting the executable. When enabled during development, startup points to the development apphost; publishing first is recommended. Windows Task Manager's Startup Apps can also disable startup.

## Verification by milestone

Every milestone was compiled with warnings treated as errors. A local smoke test stayed responsive at approximately 139 MiB working set with zero measured CPU seconds over a five-second idle sample; a duplicate launch exited with code 0. These short, unconnected measurements do not establish long-term usage with real accounts. Run `dotnet run` and check:

1. **Shell:** top-center placement, hover and leave delays, click expansion, click-through outside the rounded shape, no taskbar/Alt+Tab entry, fullscreen hiding, display scaling changes, duplicate launch exiting.
2. **Settings:** save/reopen, masked credentials, invalid interval rejection, encrypted file, blank-key disconnection.
3. **Revenue:** valid/invalid keys, multiple currencies, local midnight/month boundaries, five recent sales, refunds, provider tooltips, network failure and manual retry.
4. **Calendar:** Google testing consent, cancel/timeout, restart with refresh token, tomorrow and all-day events, event/Join links, five-minute alert.
5. **Gmail:** unread count, metadata links, new important mail, read/unread changes, reconnect, expired history resync.
6. **Polish:** eight-second activity fades, queued five-second alerts, pause, disabling cards, startup registration and removal.

Run `dotnet run -- --self-test` for deterministic currency/date/DPAPI checks. Desktop interactions and real account requests require interactive Windows testing with your own credentials. Resource goals (~150 MB and near-zero idle CPU) are targets to measure on your PC, not guaranteed limits. The spring rendering handler is unsubscribed when settled; lightweight timers check fullscreen once per second, activities every eight seconds, and data schedules every 30 seconds.
