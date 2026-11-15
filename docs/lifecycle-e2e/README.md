# Full lifecycle through the real browser - 13 November 2026

`tests/lifecycle_e2e.py` drives the whole procurement chain with Playwright against the real app, real SQL Server and live Ollama (gemma3:4b):
request -> tender (4 seats) -> weights -> bidding (+ re-tender) -> 3 supplier offers -> compliance -> AI analysis -> **meeting** -> 4 independent evaluations
-> ranking + signed PDF report -> 4 committee signatures -> Manager signature -> 4 Top Committee votes with signatures (3 approve / 1 reject) -> final decision notice
-> Manager acknowledgement -> Officer award -> Officer "Notify Suppliers" -> supplier bells. Seed accounts: `Tests/AuthSmoke` `--seed-lifecycle`.

Result: **57/57** (Jitsi mode). Zoom mode (`MEETING_MODE` unset, fake Zoom OAuth/API on 127.0.0.1:9911 with the real `ZoomMeetingService`): scheduling, credentials rejection, meeting creation,
join-link notifications and redirects all passed (`tests/zoomjoin.py` 8/8); the remaining steps are identical. `tests/toppages.py`: Top Committee pages 11/11.

## Meetings
- The system now creates the meeting link itself when the Officer schedules the evaluation (box ticked by default): **Zoom** when `Zoom:*` is configured, otherwise a private **Jitsi** room (`meet.jit.si/OrangeTender<id>-<random>`).
  The Officer can still paste a Zoom link. The link goes to the Officer and the four evaluators as a notification ("Join meeting") when scheduled and again when scoring opens; `/Meetings/Join/{tender}` opens the Jitsi room inside the system (from the `meetingZoom` sample) or redirects to Zoom. Everyone else gets 404.
- Real Zoom needs your credentials: `dotnet user-secrets set "Zoom:AccountId" ...` (also `ClientId`, `ClientSecret`, `HostUserId`) in the web project. Not verified against zoom.us itself (no credentials here).

## Top Committee
SaraD's `TopCommitteeService` records votes without signatures and awards automatically on unanimity, so it was not used. The pages (`Areas/TopCommittee`) are read-only over the signed workflow: results, voting entry, history. Voting/signing stays on the signed report page.
