# Team accounts and demo data

## Team development accounts (automatic)

`git pull origin WorkV3`, open `Orange-Tendering-System.slnx`, run the **https** profile in Visual Studio. On every
Development start the app migrates the team database and then `Services/TeamAccountSeeder.cs` creates or repairs these
eleven internal accounts and thirteen suppliers, and verifies that each one can sign in. Password for all: **`OrangeDemo2026!`**

| Role | Email | Committee seat |
| --- | --- | --- |
| Admin | `admin@orange-demo.local` | — |
| Requesting Department (Team Demo Department) | `department@orange-demo.local` | — |
| Sourcing Officer | `sourcing.officer@orange-demo.local` | — |
| Sourcing Manager (4th Evaluation Committee member) | `sourcing.manager@orange-demo.local` | `evaluation-manager` |
| Technical evaluator | `evaluation.technical@orange-demo.local` | `evaluation-technical-1` |
| Financial evaluator | `evaluation.financial@orange-demo.local` | `evaluation-financial` |
| Department Representative (Team Demo Department) | `evaluation.department@orange-demo.local` | `evaluation-department` |
| Top Committee — Chief Executive Officer | `top.ceo@orange-demo.local` | `top-ceo` |
| Top Committee — Chief Financial Officer | `top.cfo@orange-demo.local` | `top-cfo` |
| Top Committee — Chief Technology Officer | `top.cto@orange-demo.local` | `top-cto` |
| Top Committee — Chief Operations Officer | `top.coo@orange-demo.local` | `top-coo` |

Choose **Other User** on the sign-in page for these accounts.

The six supplier accounts are `supplier1@orange-demo.local` through `supplier6@orange-demo.local`.
Seven more supplier accounts are `supplierA20@orange-demo.local` through `supplierA26@orange-demo.local`.
Choose **Supplier** on the sign-in page. Each has an approved demo company and can sign in with the
shared password. Supplier workspace access still follows the normal face or passkey enrollment step.

What startup does (no manual SQL, resets, activation emails or database deletion):

- Creates missing accounts; repairs managed ones: password (only when it no longer matches), email confirmation,
  activation, "must change password", lockout and failed attempts, roles (`SystemRoles.AccountRoles`), committee seat
  (`CommitteeAccountSeats`) and the department. Account ids and workflow history are kept.
- Managed accounts carry the claim `orange:team-account`. Accounts from the first version of the seeder are adopted.
  An unrelated account that uses one of these emails is never taken over: startup stops and says what to do.
- Exactly one Sourcing Officer may be active. Synthetic `@orange-demo.local` officers ("Demo …"/"Team …") are
  deactivated; a real active officer is never changed, startup stops with instructions instead.
- The log shows the database used, what was created or repaired, and verifies all 24 managed accounts.
  If anything cannot be fixed, startup fails with the reason; it never reports accounts that do not work.

## The team database and safety limits

- The `http` and `https` profiles set `TeamDev__Database=OrangeTenderingTeamDev`. In Development that name replaces the
  database of your own `Default Connection`; your SQL Server instance and login (appsettings or User Secrets) are kept.
- Fixed-password accounts and demo fixtures are created only when the environment is Development **and** the database
  is `OrangeTenderingTeamDev` (or `OrangeTenderingTeamDev_*`) on a local server. Never in Production or other databases.
- `TeamAccounts:Enabled=false` turns provisioning off for a run (used by the AuthSmoke tests); on the team database the
  log then warns that the accounts were not provisioned.

### Your existing databases

Nothing was deleted or changed. `OrangeTenderingSystem` (and any other local database) keeps its data:

- In Visual Studio choose the profile **"https (existing OrangeTenderingSystem database)"** to run against it, or
- open it in SQL Server Object Explorer / SSMS on `localhost`.

The team accounts above exist only in `OrangeTenderingTeamDev`.

## Optional demo fixtures (`DemoData:Enabled`)

Separate from the team accounts. With `DemoData__Enabled=true` (team database only) `DemoDataSeeder` adds ten approved
demo suppliers (`supplier1@orange-demo.local` … `supplier10@orange-demo.local`, password `OrangeDemo2026!`), purchase
requests, tenders, offers, clarifications and reports. All internal roles in those fixtures are the team accounts above;
the seeder no longer creates its own officer, manager, evaluators or Top Committee members. Databases seeded by older
versions keep their historical rows and accounts (`officer@`, `committee1@`, `top1@` …); only new rows use the team accounts.

Each supplier must enroll a face or passkey through the normal supplier setup before entering the protected supplier
workspace. Top Committee members vote on a tender only after an Admin assigns the four seats for that tender.

## Tests

- `dotnet Tests/AuthSmoke/bin/Release/net10.0/AuthSmoke.dll --team-account-checks` — real SQL Server, provisioning on:
  fresh database, previous-seeder accounts, changed passwords, disabled/unconfirmed/locked accounts, wrong roles and
  seats, competing officers, demo data, repeated starts, unrelated data, Production, reserved-email collisions, database
  isolation, and HTTPS sign-in for all ten accounts through the login form and the page script. It creates and drops its
  own `OrangeTenderingTeamDev_Check_*` databases.
- `AuthSmoke.dll` (no arguments) — the original authentication suite on an `OrangeAuthCheck_*` database (see its README).
