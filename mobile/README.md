# OREVOSH on the App Store and Google Play

The web app already installs to the home screen and behaves like a native app (README, "On a phone"). The stores
need a native binary, so this folder wraps the production site in a [Capacitor](https://capacitorjs.com) shell: a
WebView that loads `https://looks.example.com` (replace with the real origin, `DEPLOY.md`), with the app's colours
on the splash and the status bar. Nothing of the web app is copied into the binary: a deploy updates the store apps
too, and the store version only changes when the shell changes. Nothing is installed here; `npm install` in this
folder on a machine with the SDKs below does it.

## Why a remote-URL shell, and what it costs

The shell points at the live site (`server.url` in `capacitor.config.json`) instead of bundling `wwwroot`, because the
app is a server app: the API, the cookies, the media routes and the service worker all live at one origin, and a
bundled copy would still have to talk to it. What the choice implies:

- **Offline:** the shell shows nothing without a network on first launch (the site's own offline banner appears after
  that, when the service worker is registered). Acceptable for an app that needs the server for every check.
- **Service worker on iOS:** WKWebView runs service workers only for domains listed under `WKAppBoundDomains` in
  `Info.plist` together with `limitsNavigationsToAppBoundDomains` (set in the config): add `looks.example.com` there.
- **Push:** Web Push does not work inside WKWebView, and Android's does not either in a Capacitor WebView. The web app's
  push (`/api/push/*`) keeps working in the browser install; native push (APNs and FCM through `@capacitor/push-
  notifications`) needs a server side that sends to both, which does not exist yet. Ship without push in the shells,
  or build that first.
- **Camera and files:** `getUserMedia`, `MediaRecorder`, `<input type="file">` and the share sheet work in both
  WebViews with the permission strings below; the in-app camera and the story card need no plugin.
- **Payments:** Pro is sold with Stripe on the web. The stores forbid that inside a native app (`STORE.md`,
  "Payments"): hide the purchase in the shells or add StoreKit and Play Billing before submitting.
- **Apple's minimum-functionality rule (guideline 4.2):** a WebView that only shows a website is rejected. OREVOSH is a
  full app (camera, feed, notifications, account) so the rule is usually met, but the reviewer must see it: the notes
  in `STORE.md` explain what the app does, and the first launch must land on something worth seeing (the feed), not a
  login wall.

## Accounts and fees

| | Apple | Google |
|---|---|---|
| Developer account | Apple Developer Program, 99 USD a year, needs a D-U-N-S number for a company or an Apple ID for an individual; enrollment takes a few days | Google Play Console, 25 USD once; a new personal account must run a closed test with 12 testers for 14 days before it can publish (rule since 2023) |
| Machine | a Mac with Xcode 16 or newer (iOS 15+ target) | any machine with Android Studio (Ladybug or newer), JDK 17 |
| Signing | certificates and profiles from App Store Connect, managed by Xcode | an upload keystore you create once and keep forever (`keytool`); Play App Signing holds the app key |
| Review | 1–3 days, human; rejections come with a guideline number | hours to a few days, mostly automated; a policy questionnaire per release |
| Bundle id | `app.orevosh.mobile` (change in `capacitor.config.json` before `cap add`; it cannot change later) | same |

## Build for Android

```bash
cd mobile
npm install
npx cap add android                 # once; creates android/ (git-ignore it or commit it, both are common)
npx cap sync                        # after every change to capacitor.config.json
npx cap open android                # Android Studio
```

In Android Studio: Build → Generate Signed Bundle (AAB) with the upload keystore; the `versionCode` and `versionName`
live in `android/app/build.gradle`. Icons: run Android Studio's Image Asset tool on
`src/FitCheck.Api/wwwroot/icons/icon-512.png` (foreground) with `#0b0b0f` as the background, or use the maskable
icon. Splash: `@capacitor/splash-screen` with the background colour from the config and
`brand-kit/logos/mark-1024-dark-transparent.png` as the image. Permissions in `AndroidManifest.xml`: `CAMERA`,
`RECORD_AUDIO` (clips with sound), `INTERNET`; `WRITE_EXTERNAL_STORAGE` is not needed (the share sheet saves the
card). Then Play Console → Create app → the listing from `STORE.md` → Testing → Closed testing first, Production after.

## Build for iOS

```bash
cd mobile
npm install
npx cap add ios                     # once; creates ios/ (needs a Mac with Xcode and CocoaPods)
npx cap sync
npx cap open ios                    # Xcode
```

In Xcode: set the team and the bundle id, the deployment target (iOS 15), the display name `OREVOSH`, the app icon
from `brand-kit/logos/mark-1024-dark.png` (1024×1024, no alpha, no rounded corners: Apple rounds it), and in
`Info.plist`:

| key | value |
|---|---|
| `NSCameraUsageDescription` | OREVOSH uses the camera to photograph or film your outfit for a check. |
| `NSMicrophoneUsageDescription` | OREVOSH records sound with your clips. |
| `NSPhotoLibraryAddUsageDescription` | OREVOSH saves your story card to your photos. |
| `NSPhotoLibraryUsageDescription` | OREVOSH lets you pick an outfit photo or clip from your library. |
| `WKAppBoundDomains` | an array with `looks.example.com` |
| `UISupportedInterfaceOrientations` | portrait only (the app is portrait) |
| `ITSAppUsesNonExemptEncryption` | `NO` (standard HTTPS only) |

Hebrew: add `he` to the project's localizations so the system UI (share sheet, permission dialogs) follows the
device language; the web app picks its own. Product → Archive → Distribute → App Store Connect → TestFlight first.
Then App Store Connect → the listing from `STORE.md`, the screenshots from `brand-kit/store/`, the review notes and the
test account, Submit.

## Review notes to keep at hand

- The test account and the reviewer notes are in `STORE.md`, "What to prepare for review"; paste them as they are.
- The block feature Apple expects in a UGC app is not in the app yet (`STORE.md`); plan it before the first iOS
  submission.
- If the review asks why the app is a WebView: it is the same app as the web, the camera, the clips, the feed and the
  notifications are its own, and the shell exists so people who look in the store find it.
- After a rejection, reply in Resolution Center with what changed, then upload a new build; do not resubmit the same
  binary.

## Not done here

No Capacitor is installed and no `android/` or `ios/` folder exists in the repository; the two files here are the
config and the dependency list. Native push, store billing, the block feature and real age assurance (README, "Known
limitations") are the four things between this shell and a public listing.
