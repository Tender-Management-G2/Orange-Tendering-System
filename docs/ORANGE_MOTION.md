# Orange Motion — shared action feedback

One orange (six citrus segments, a leaf, a highlight) confirms every meaningful server-side action in every role.
It processes while the request runs, assembles, merges into the whole fruit and shows what actually happened.
**Feedback is only shown for a result the server reported.** A click alone never shows success.

## Architecture

| Piece | File | Role |
|---|---|---|
| Engine + popup + toast + inline | `wwwroot/js/orange-motion.js` | `window.OrangeMotion`; the only animation code |
| Styles (light/dark, mobile) | `wwwroot/css/orange-motion.css` | the only Orange Motion stylesheet |
| Layout host | `Views/Shared/_OrangeMotion.cshtml` | loads the CSS/JS and plays a one-time server signal; rendered once by every layout |
| Server signal | `Services/OrangeMotion.cs` | `OrangeFeedback`, `TempData.Orange(...)`, `OrangeMotionResultFilter` |
| Workflow catalog | `Services/OrangeWorkflowFeedback.cs` | messages for every tender workflow and report action |
| Committee facts | `Services/TenderWorkflowService.Feedback.cs` | read-only count of approvals in the current committee round |

Every layout (`_AdminLayout`, `_DepartmentLayout`, `_EvaluationCommitteeLayout`, `_SourcingOfficerLayout`,
`_SourcingManagerLayout`, `_SupplierLayout`, both `_TopCommitteeLayout`s, `_WorkspaceLayout`, `_Layout`) renders
`<partial name="_OrangeMotion" />` before its `Scripts` section. Do not include the CSS/JS in individual views.

## Variants and presentation

| Variant | Symbol | Use for |
|---|---|---|
| `save` | checkmark | drafts, updates, records saved |
| `submit` | checkmark | official submissions |
| `send` | paper plane → checkmark | sending to another role, messages, invitations |
| `upload` | arrow → checkmark | files |
| `approve` | checkmark | approvals, acceptances |
| `return` | return arrow (no checkmark) | returns, rejections, declines — a recorded rejection is **not** an approval |
| `error` | cross + shake | failures; a popup error stays until closed |
| `committee` | 4 segments, one per member; lit = approvals | committee votes. Full approval (merge + checkmark) only when `approvals == required` for the same round |
| `award` | trophy + triple ripple | final tender award only |

| Mode | When | Behaviour |
|---|---|---|
| `popup` | submissions, official reports, important approvals and transitions, final decisions, award | centred, blocks briefly, auto-dismisses |
| `toast` | drafts, updates, routine messages, single scores, frequent operations | bottom-right (bottom on mobile), non-blocking, pauses on hover |
| `inline` | uploads, background work, AI analysis, long-running work | inside a container you name; stays as a status line |

No feedback for navigation, opening pages, filtering, typing or buttons that do not change server state.
Validation errors are never covered: the re-rendered form with its messages is shown as is.

## Timing (global, `OrangeMotion.config`)

`enter 150 · assemble 240 · transform 340 · holdPopup 950 · exit 180` ms → about 1.7 s after the server answers.
`holdToast 2400` (non-blocking), `toastDelay 140` (a fast request shows no processing toast at all).
Processing lasts exactly as long as the request; there are no artificial delays. Change values only in `config`.
`prefers-reduced-motion` replaces movement with short fades.

## From a controller (Post-Redirect-Get)

```csharp
using Orange_Tendering_System.Services;

await service.Save(...);                                   // only after the operation committed
TempData.Orange(OrangeFeedback.Popup("Request sent!", $"PR-{id} is with the Sourcing Officer.", OrangeVariant.Send));
return RedirectToAction(nameof(Details), new { id });
```

`OrangeFeedback.Toast(title, description, variant)` for lightweight results;
`OrangeFeedback.CommitteeApproval("Top Committee", approvalsInThisRound)` for votes — it reports full approval only at 4/4.
The signal is TempData: it survives the redirect and is consumed when shown, so a refresh never replays it.
Keep failures in the existing persistent error messages (they must stay visible); do not also set a success signal.

AJAX endpoints return the same object: `return Json(new { ok = true, feedback = feedback.ToJsonElement() });`

Tender workflow actions need nothing extra: `WorkflowControllerBase.Execute` and `ReportControllerBase.Execute` look up
`OrangeWorkflowFeedback`. Add a line there for a new action; set `ActionFeedback` inside `Execute` when the message
needs facts only the action knows (for example the number of invited suppliers).

## From JavaScript

```js
OrangeMotion.show({ title: 'Vote recorded', variant: 'committee', approvals: 2, required: 4, mode: 'popup' });

const motion = OrangeMotion.begin({ mode: 'toast', title: 'Working…' });   // when the request starts
const res = await fetch(url, { method: 'POST', body, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
const data = await res.json();
if (res.ok && data.ok) motion.finish(data.feedback); else { motion.cancel(); showYourPersistentError(data.error); }
```

`begin({ mode: 'inline', target: '#upload-status' })` renders inside that element.

## A new form, without JavaScript of your own

```html
<form asp-action="Create" method="post" data-om="popup" data-om-working="Sending your request">
  <button type="submit" name="intent" value="save" data-om="toast" data-om-working="Saving your draft">Save draft</button>
  <button type="submit" name="intent" value="submit">Submit</button>
</form>
```

The form is sent with fetch (antiforgery token included). Buttons are disabled until the answer arrives, so a double
click sends one request. The global `OrangeMotionResultFilter` answers the controller's normal redirect with
`{ redirect, feedback }`; a popup plays over the current page and then the browser follows the redirect; a toast is
handed to the next page so the user never waits. A re-rendered page (validation) replaces the document with its messages.
A 4xx/5xx shows an error popup with the server's message. Without JavaScript the form posts normally and the next page
plays the same signal. Add `data-om="off"` to opt a form out.

## Messages

Say what happened: "Request sent!", "Draft saved!", "Tender created!", "Invitations sent!", "Offer submitted!",
"Report forwarded!", "Approval recorded", "Request returned for correction", "Tender awarded!". Show reference numbers,
destination roles or statuses only when the server knows them. Never say "Success!".

## Integrated operations

- **Department:** save draft (toast), submit (popup, send), edit/save changes, resubmit after return.
- **Sourcing Officer:** accept / return / reject purchase requests; create tender; request weights; approve weights;
  open bidding; set deadline; invite suppliers (count), external invitation; close bidding; new round; proceed /
  exception request; compliance (single and bulk); schedule and end evaluation; AI analysis and review; regenerate,
  send, correct and forward reports; assign Top Committee; award; notify suppliers; retry e-mails; uploads; replies.
- **Evaluation Committee:** propose weights; submit evaluation; generate report; sign/finalize (committee progress).
- **Sourcing Manager:** accept / return / reject report (workflow and report pages); proceed-exception decision;
  save and forward executive summary; acknowledge final decision; clarification threads.
- **Top Committee:** sign & approve (committee progress, full approval only at 4/4 of the same round); rejection
  recorded; private messages, clarifications, replies.
- **Supplier:** accept / decline invitation; save/update draft offer; submit offer; uploads; contact details;
  clarification create/reply; registration completed (after e-mail verification).
- **Admin:** create user, update user, resend setup link; create/update department; assign/remove role; supplier
  approve / reject / suspend; document verification; standing and tender committees; maintenance records.
- **Shared:** clarification send / reply / resolve.

Not integrated (no finished operation or not meaningful): Admin settings (read-only page), notification "mark read",
legacy compatibility POSTs that only answer 409 "this form has moved", biometric enrollment (has its own studio UI).
