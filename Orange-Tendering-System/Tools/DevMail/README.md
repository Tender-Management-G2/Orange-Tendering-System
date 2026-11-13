# Email delivery modes for local development

The web project defaults to `Smtp:Mode=LocalInbox` in `appsettings.Development.json`. This sends verification messages to a local test inbox, not to Gmail. To test actual Gmail delivery, select `Smtp` mode in Visual Studio User Secrets and provide a Gmail sender account with an app password. Do not put credentials in `appsettings*.json`, source control, screenshots, or issue reports.

## Configure real Gmail SMTP in Visual Studio

1. Enable 2-Step Verification on the Google account that will **send** email. Create a Google App Password for this application. The normal Google account password is not the SMTP password. Google may disable App Passwords for some managed or Advanced Protection accounts.
2. In Visual Studio, right-click the **Orange-Tendering-System** web project (the project containing `Orange-Tendering-System.csproj`) in Solution Explorer and choose **Manage User Secrets**. The project already has a `UserSecretsId`; do not run `dotnet user-secrets init`.
3. In the opened `secrets.json`, add or merge the following `Smtp` object. Replace the two descriptive values with the Gmail sender address and its app password. Use the same sender Gmail address for `From` and `Username`. Keep any other existing secrets in the file.

```json
{
  "Smtp": {
    "Mode": "Smtp",
    "Host": "smtp.gmail.com",
    "Port": 587,
    "From": "YOUR_SENDER_GMAIL_ADDRESS",
    "Username": "YOUR_SENDER_GMAIL_ADDRESS",
    "Password": "YOUR_GOOGLE_APP_PASSWORD",
    "AllowInsecureLoopback": false
  }
}
```

4. Save `secrets.json`, stop any running web application, and run the **https** launch profile again with `ASPNETCORE_ENVIRONMENT=Development`. The app reads User Secrets at startup. `Smtp:Mode=Smtp` switches away from the local inbox and uses STARTTLS on port 587. The Node local inbox does not need to run.

## End-to-end test

1. Open `https://localhost:7182/Identity/Account/Register` and register a new supplier with a real inbox you control as the contact email. You may use the sender Gmail address as the recipient for this test. If an unverified supplier already exists, wait at least one minute and use **Send a new code** on its Verify Email page instead.
2. Check that the Verify Email page no longer links to the local inbox. Check the real recipient's Gmail Inbox and Spam folders for the message. A message appearing in the Node inbox is **not** proof of real delivery.
3. Enter the six-digit code from the real Gmail message into Verify Email within 10 minutes. Success redirects to **Pending Approval** with an Identity sign-in cookie. Sign out, then sign back in using email and password; the Pending Approval page should open without another OTP. Do not put the code in logs, screenshots, or source control.
4. If registration fails, read the non-secret error on the page. Confirm the app runs in Development, that User Secrets belong to the web project, that the sender Gmail account has an active App Password, and that outbound port 587 is reachable. Never paste your app password or OTP into a chat or issue.

Real delivery is verified only after the recipient observes the email in Gmail and completes verification. SMTP acceptance or a passing build alone is insufficient.

Registration first saves a provisional account and uploaded documents so that a code can be resent. It is not submitted to Admin review, cannot be approved, and cannot sign in until the contact email is verified. After verification, the supplier is signed in but remains on Pending Approval; tender actions require Admin approval. If you already started registration, use **Already started? Verify your email or request a new code** on the registration page. Server validation errors keep the selected files in the open browser form so you can correct a field without attaching them again.

## Local inbox mode

For synthetic testing, remove the `Smtp` override from User Secrets or set `Mode` to `LocalInbox` with the default loopback host, no username or password, then run `node Tools/DevMail/server.js`. The inbox is at http://127.0.0.1:8025/. It binds only to loopback and stores messages in memory until the process stops.

References: [Google SMTP server settings](https://support.google.com/mail/answer/7104828?hl=en), [Google App Passwords](https://support.google.com/mail/answer/185833?hl=en), and [Visual Studio User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?tabs=visual-studio&view=aspnetcore-10.0).
