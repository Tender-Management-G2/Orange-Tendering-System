# Committee presentation setup

Prepares the local team database `OrangeTenderingTeamDev` for the committee presentation: a clean database (same schema),
fourteen accounts, and seven tenders, each stopped one step before the action shown live. Everything is created through the
application's own services, so every state is one the app itself produced. App code is not changed.

## Accounts (password for all: `OrangeDemo2026!`)

| Name | Email | Role |
| --- | --- | --- |
| Haya | haya@gmail.com | Admin |
| Sara | sara@gmail.com | Sourcing Officer |
| Majed | majed@gmail.com | Sourcing Manager (4th Evaluation Committee member) |
| Tariq | tariq@gmail.com | Evaluation Committee, Department Representative (Information Technology) |
| Saja | saja@gmail.com | Evaluation Committee, Technical Representative |
| Amal | amal@gmail.com | Evaluation Committee, Financial Representative |
| Anfal | anfal@gmail.com | Top Committee, CEO |
| Mohammad | mohammad@gmail.com | Top Committee, CFO |
| Batool | batool@gmail.com | Top Committee, CTO |
| Hadeel | hadeel@gmail.com | Top Committee, COO |
| Sameh | sameh@gmail.com | Requesting Department (Information Technology) |
| Ali | ali@gmail.com | Supplier, Ali Technology Supplies |
| Mosa | mosa@gmail.com | Supplier, Mosa Computer Systems |
| Omar | omar@gmail.com | Supplier, Omar IT Solutions |

## Steps (run from this folder; the app must not be running)

1. Back up the database first:
   ```powershell
   sqlcmd -S localhost -E -C -b -Q "BACKUP DATABASE [OrangeTenderingTeamDev] TO DISK=N'C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\Backup\OrangeTenderingTeamDev_before_wipe.bak' WITH COPY_ONLY, INIT, CHECKSUM"
   ```
   (Use your own SQL Server backup folder if the instance name differs.)
2. Clear all data, keeping the schema, migration history, roles, procurement categories and real departments:
   ```powershell
   sqlcmd -S localhost -E -C -b -I -d OrangeTenderingTeamDev -i Sql\wipe_data.sql
   ```
3. Stop the app from re-creating the old `@orange-demo.local` team accounts on start (local User Secret, not code):
   ```powershell
   dotnet user-secrets set "TeamAccounts:Enabled" "false" --project ..\..\Orange-Tendering-System.csproj
   ```
4. Create the accounts and the standing Evaluation and Top committees:
   ```powershell
   dotnet run --project AccountSetup
   ```
5. Create the seven tenders (needs Ollama running locally with `gemma3:4b` for the AI offer analysis):
   ```powershell
   dotnet run --project TenderSetup -- OrangeTenderingTeamDev
   ```
   The tool is idempotent: running it again skips what already exists.
6. Back up the ready state so it can be restored after a rehearsal:
   ```powershell
   sqlcmd -S localhost -E -C -b -Q "BACKUP DATABASE [OrangeTenderingTeamDev] TO DISK=N'C:\Program Files\Microsoft SQL Server\MSSQL17.MSSQLSERVER\MSSQL\Backup\OrangeTenderingTeamDev_presentation_ready.bak' WITH COPY_ONLY, INIT, CHECKSUM"
   ```

`TenderSetup/make_assets.py` regenerates the offer PDFs and signature images in `TenderSetup/assets` from `tenders.json`.

## Presentation order

| # | Tender | Stopped at | Live action | Account |
| --- | --- | --- | --- | --- |
| 1 | PR-1 48-port PoE+ network switch (12) | Purchase request accepted, no tender yet | Create the tender and send it to the committee | Sara |
| 2 | T-0001 Business laptop (25) | Majed, Tariq and Amal proposed weights | Saja proposes; the average appears | Saja |
| 3 | T-0002 A3 colour multifunction printer (10) | All four proposals in (40% financial / 60% technical) | Approve, choose the closing date in stage 4, publish and invite | Sara |
| 4 | T-0003 Wi-Fi 6 indoor access point (40) | Published; Ali and Mosa submitted; Omar accepted | Omar submits his offer | Omar |
| 5 | T-0004 10 kVA online UPS (4) | Three offers received, bidding open | Close bidding, run the AI analysis, schedule the meeting | Sara |
| 6 | T-0005 Engineering desktop workstation (15) | Meeting open; Majed, Tariq and Amal scored and signed | Saja scores and signs; report and ranking appear | Saja |
| 7 | T-0006 Meeting room video conferencing kit (8) | Report signed, executive summary signed, three Top approvals | Hadeel approves; the tender is awarded | Hadeel |

Notes:
- Sign in from `https://localhost:7182/Identity/Account/Login` directly; a redirect with `?ReturnUrl=` currently fails to sign in.
- Suppliers must enrol a face or passkey on the presentation laptop before entering the supplier workspace.
- Open tenders close on 1 December 2026, 23:00 Jordan time.

## Image credits (Wikimedia Commons)

| File | Source | Author | Licence |
| --- | --- | --- | --- |
| switch.jpg | [Cisco Catalyst 2960-S (11625112186).jpg](https://commons.wikimedia.org/wiki/File:Cisco_Catalyst_2960-S_(11625112186).jpg) | Dmitry Grigoriev | CC BY 2.0 |
| laptop.jpg | [Lenovo ThinkPad X1 Carbon Ultrabook.jpg](https://commons.wikimedia.org/wiki/File:Lenovo_ThinkPad_X1_Carbon_Ultrabook.jpg) | Elroygoh | CC BY-SA 4.0 |
| printer.jpg | [Xerox AltaLink C8045 photocopier.jpg](https://commons.wikimedia.org/wiki/File:Xerox_AltaLink_C8045_photocopier.jpg) | Baron Maddock | CC BY 4.0 |
| accesspoint.jpg | [Unifi AC Lite Access Point.jpg](https://commons.wikimedia.org/wiki/File:Unifi_AC_Lite_Access_Point.jpg) | ServerGuruBrisbane | CC BY-SA 4.0 |
| ups.jpg | [UPS Image.JPG](https://commons.wikimedia.org/wiki/File:UPS_Image.JPG) | Zafijan | Public domain |
| workstation.jpg | [Dell Precision T 3620 -Workstation- (48642527998).jpg](https://commons.wikimedia.org/wiki/File:Dell_Precision_T_3620_-Workstation-_(48642527998).jpg) | Dr GMC | CC BY-SA 2.0 |
| videoconf.jpg | [Gesto Communications Videoconference Room.jpg](https://commons.wikimedia.org/wiki/File:Gesto_Communications_Videoconference_Room.jpg) | Petr Faltus | CC BY 3.0 |

Images were resized to at most 1000 px. Offer PDFs, company stamps and signatures are generated by `make_assets.py`.
