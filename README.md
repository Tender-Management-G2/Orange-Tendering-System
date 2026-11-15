# Orange-Tendering-System

For local SQL Server, EF Core migrations, development login setup, and the team's migration workflow, see [Team setup](docs/TEAM_SETUP.md).

## Run WorkV1 locally

From the `Orange-Tendering-System` web project directory, use .NET 10 and a local SQL Server. Set your own connection string and initial Admin credentials in User Secrets if they are not configured already (see Team setup). The app applies pending migrations when launched in Development.

In one terminal, start the local email inbox:

```powershell
node Tools/DevMail/server.js
```

In a second terminal, start the app:

```powershell
dotnet run --launch-profile https
```

Open `https://localhost:7182`. Sign in as the bootstrap Admin and change the initial password. Create employees at `/Admin/Users/Create`: enter or generate a temporary password, then open their welcome message at `http://127.0.0.1:8025`. The employee must follow the one-hour setup link to set a private password before signing in. The temporary password is never emailed.

Suppliers register at `/Identity/Account/Register`, verify the emailed OTP, enroll face and passkey sign-in, and wait for Admin approval. After approval they can open the Supplier workspace with biometric sign-in. The Sourcing Officer tender page is `/SourcingOfficer/Workflow`; the merged workflow continues through committee evaluation, Sourcing Manager review, Top Committee decision, and supplier notification.

Development uses the loopback inbox by default. To deliver to a real mailbox, configure authenticated SMTP in User Secrets as described in [Email delivery modes](Orange-Tendering-System/Tools/DevMail/README.md). A real delivery still requires valid sender credentials and a reachable SMTP server.
