# KUDUBROWSE

A private everyday web browser for Windows, built on Microsoft's WebView2 (Edge's engine): tabs, ad and tracker
blocking, passwords, importing from Chrome, rooms with separate logins.
Part of the KUDU family of free, open-source (MIT) Windows apps by Azeem Bukhari.

## Working with Azeem (the owner)

- **Branches:** work on `main` and push to `main`. Ask before creating any other branch.
- **New features:** first say what you understood, and ask your doubts and suggestions, one question at a time.
  Only start building when Azeem says "build".
- **Step by step:** Azeem asked for this app to be built one step at a time, stopping after each step to report,
  to keep the work small. Don't run ahead to the next step without being asked.
- **Look:** keep the app's current look. No redesigns unless asked.
- **About window credit:** "Made by Azeem, with Claude and ♥" (the heart in the accent colour). Keep it.
- **Commits:** author `Azeem Bukhari <12558389+azeembukhariprivate@users.noreply.github.com>`. Clear messages that say
  what changed and why. Don't put AI model names or versions in commits, code or docs.
- **Words:** the app's text, README and release notes use plain everyday words, not technical jargon.
- **Before pushing:** build and run the tests locally, and re-read the diff. After pushing, watch the GitHub Actions
  build until it's green; fix failures, never skip or disable a test to get green.

## The plan (agreed with Azeem)

1. Done (0.1): window, tabs, address bar with search engines, back/forward, new-window links as tabs, closed tabs,
   tabs restored next time (loaded when clicked), zoom, print, full screen, settings, about.
2. Done (0.2): ad and tracker blocking. `Core/Blocking/FilterEngine.cs` + `NetworkRule.cs` read the lists (rules it
   can't follow exactly are left out); `Views/Blocker.cs` checks every request (WebResourceRequested) and hides page
   parts (general CSS added at document creation, site rules at DOMContentLoaded). Lists come with the app (the build
   downloads them; not in git) and are fetched again every 4 days into `%LocalAppData%\KUDUBROWSE\Lists`.
   Pop-ups not started by a click are blocked; the shield button allows a site (its registrable domain).
3a. Done (0.3): bookmarks (`Core/Library/Bookmarks.cs`, bookmarks.json), history (`History.cs`, history.txt, 90 days),
   address-bar suggestions (`Suggestions.cs`), Chrome import (`ChromeImport.cs`: Bookmarks JSON; History via our own
   read-only `SqliteReader.cs`; tests use `tests/KUDUBROWSE.Tests/Fixtures/ChromeUserData`, made by
   `tools/make-chrome-fixture.py`), default-browser registration (`Core/ShellIntegration.cs`, HKCU only; Windows makes the
   person choose), welcome window. Open tabs from Chrome were left out (Chrome's session files aren't documented).
3b. Done (0.4): passwords. `Core/Passwords/Vault.cs` (one file, passwords.dat, encrypted with DPAPI for the user via
   `Views/PasswordStore.cs`; logins matched by exact origin, then same registrable domain and scheme),
   `PasswordTools.cs` (generator, weak check, Pwned Passwords k-anonymity, CSV import). `Views/PasswordScript.cs` runs in
   top-level pages: reports focused login boxes and sent logins; it's only sent a login after the person picks one in
   the browser's own WPF list (never automatic fill). `Verify` asks Windows Hello (UserConsentVerifier interop), else
   the Windows password (CredUI + LogonUser), then not again for a minute.
4. Rooms (WebView2 profiles), private windows, tab groups, sleeping tabs, side-by-side pages, reading mode
   (Mozilla Readability), full-page screenshot and PDF, downloads, one running copy, installer. Then release 1.0.

Versions below 1.0 are built and tested by CI but never released (see "Is this a new version?" in the workflow).

## How the app is built

- .NET Framework 4.8 WPF app. The screens are built in C# (no XAML compiler); the themes (`Themes/Light.xaml`,
  `Dark.xaml`, `Styles.xaml`) are XAML loaded at startup as embedded resources, using resource keys such as `Accent`,
  `Text`, `SurfaceBg`. Use the helpers in `Controls/Ui.cs` and existing keys rather than hard-coded colours.
- Pages are WebView2 controls (`Views/BrowserTab.cs`), one per tab, made the first time a tab is shown. All tabs share
  one engine and profile (`Views/Web.cs`, `%LocalAppData%\KUDUBROWSE\Profiles\Default`).
- Logic that doesn't need Windows lives in `Core/` (address bar, session) and is tested by `tests/KUDUBROWSE.Tests`
  (a .NET 8 console program).
- `KUDUBROWSE.exe --self-test` drives the real browser on Windows with test pages it makes itself, using its own
  profile and session file (GitHub Actions runs it, keeping screenshots as a build download). Add a self-test step
  for new screens and features.
- Settings live in `HKEY_CURRENT_USER\Software\KUDUBROWSE`. The Inno Setup installer (`installer/KUDUBROWSE.iss`)
  installs per user, without administrator rights.
- The browser itself collects nothing. Never add anything that sends data about the user.

```
dotnet run --project tests/KUDUBROWSE.Tests -c Release
dotnet build src/KUDUBROWSE/KUDUBROWSE.csproj -c Release -o out/KUDUBROWSE
out\KUDUBROWSE\KUDUBROWSE.exe --self-test        (Windows only)
```

## Shared code (kudu-common)

The files listed in `.kudu-common` (the look and controls in `Controls/`, `Core/Admin.cs`, `AppInfo.cs`, `Args.cs`,
`ThemeManager.cs`, `Unelevated.cs`, `UpdateChecker.cs`, `Views/Dialogs.cs`, `Views/Screenshots.cs`, ...) are copied from
[kudu-common](https://github.com/azeembukhariprivate/kudu-common) and start with a comment saying so.
**Don't edit them here**: change them in kudu-common, then run `python sync.py ../kudubrowse` there and commit both
repos. The build fails when a copy differs from kudu-common. App-specific details go in `Core/AppDetails.cs`.

## Releasing (from 1.0)

1. Raise `<Version>` in `src/KUDUBROWSE/KUDUBROWSE.csproj` (and `MyAppVersion` in `installer/KUDUBROWSE.iss`).
2. Commit and push to `main`.
3. GitHub Actions (`.github/workflows/build.yml`, Windows) tests and builds the app. When no release exists yet for that
   version it creates the `v` tag and publishes the installer and portable zip on the Releases page, plus a copy named
   `KUDUBROWSE-setup.exe`.
