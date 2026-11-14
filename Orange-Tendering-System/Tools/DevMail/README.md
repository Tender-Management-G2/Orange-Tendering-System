# Local supplier registration email

Development now sends Identity emails to a local, in-memory SMTP inbox. No mailbox account or SMTP password is needed for local testing.

From the web project directory, run this in one terminal:

```powershell
node Tools/DevMail/server.js
```

Run or restart the web application with the `https` launch profile in another terminal or from Visual Studio. Open [the local inbox](http://127.0.0.1:8025), submit the supplier registration form, and read the six-digit verification code there. Any syntactically valid test email address can be used locally. The supplier still needs Admin approval after email verification.

The SMTP and inbox ports bind to `127.0.0.1` only. Messages are held in memory and disappear when the process stops. Production deployments continue to require a real STARTTLS SMTP provider configured through secrets or environment variables; the local inbox setting exists only in `appsettings.Development.json`.
