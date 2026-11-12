"use strict";
// All data in evaluation and clarification interfaces is explicitly illustrative.
// Integration boundaries: no credentials, messages, or live statistics are simulated.
const stageDetails = [
  [
    "Define the need.",
    "Describe the requirement, establish the business context, and begin a structured purchase request.",
    "Requesting Department · Purchase Request",
  ],
  [
    "Shape the opportunity.",
    "Turn the approved requirement into a tender with clear scope, criteria, and submission requirements.",
    "Sourcing Officer · Tender",
  ],
  [
    "Bring suppliers into the process.",
    "Invite eligible suppliers to respond with technical proposals and financial bids in their own scoped workspace.",
    "Supplier · Bid",
  ],
  [
    "Evaluate with perspective.",
    "Review technical compliance and financial value. Consolidate weighted scores for an informed committee assessment.",
    "Evaluation Committee · Technical & Financial",
  ],
  [
    "Make accountable decisions.",
    "Route the evaluation outcome through the appropriate management and committee approvals.",
    "Sourcing Manager · Top Committee",
  ],
  [
    "Close with confidence.",
    "Record the authorized award outcome and bring the tender lifecycle to a documented close.",
    "Award & Closure · Tender",
  ],
];
document.querySelector(".story-steps").innerHTML = stageDetails
  .map(
    (s, i) =>
      `<article class="story-step${i === 0 ? " is-active" : ""}" data-step="${i}"><div class="story-step-head"><span class="step-number">0${i + 1}</span>${icon(stages[i][1])}</div><h3>${s[0]}</h3><p>${s[1]}</p><span class="step-tag">${s[2]}</span></article>`,
  )
  .join("");
const storySteps = [...document.querySelectorAll(".story-step")];
let activeStep = -1;
function updateStory() {
  let active = 0,
    closest = Infinity;
  storySteps.forEach((step, i) => {
    const r = step.getBoundingClientRect(),
      distance = Math.abs(r.top + r.height * 0.35 - innerHeight * 0.48);
    if (distance < closest) {
      closest = distance;
      active = i;
    }
  });
  if (active !== activeStep) {
    activeStep = active;
    storySteps.forEach((step, i) => {
      step.classList.toggle("is-active", i === active);
      step.classList.toggle("is-complete", i < active);
    });
    document.querySelector(".story-count").textContent = `0${active + 1}`;
    document.querySelector(".orbit-label").textContent = stages[active][0]
      .replace("<br>", " ")
      .toUpperCase();
    document
      .querySelector(".orbit-visual")
      .style.setProperty("--story-progress", (active + 1) / 6);
  }
}
let storyQueued = false;
addEventListener(
  "scroll",
  () => {
    if (!storyQueued) {
      storyQueued = true;
      requestAnimationFrame(() => {
        updateStory();
        storyQueued = false;
      });
    }
  },
  { passive: true },
);
addEventListener("resize", updateStory);
updateStory();

const sampleBids = [
  { name: "Supplier A", id: "DEMO-BID-001", technical: 92, price: 124000 },
  { name: "Supplier B", id: "DEMO-BID-002", technical: 86, price: 115000 },
  { name: "Supplier C", id: "DEMO-BID-003", technical: 95, price: 138000 },
];
const weightInput = document.querySelector("#technical-weight");
let currentScores = [];
function calculateScores(technicalWeight) {
  const lowest = Math.min(...sampleBids.map((b) => b.price));
  return sampleBids
    .map((b) => {
      const financial = (lowest / b.price) * 100;
      return {
        ...b,
        financial,
        technicalContribution: (b.technical * technicalWeight) / 100,
        financialContribution: (financial * (100 - technicalWeight)) / 100,
        weighted:
          (b.technical * technicalWeight) / 100 +
          (financial * (100 - technicalWeight)) / 100,
      };
    })
    .sort((a, b) => b.weighted - a.weighted);
}
function updateEvaluation() {
  const weight = Number(weightInput.value);
  currentScores = calculateScores(weight);
  document.querySelector("#technical-value").textContent = `${weight}%`;
  document.querySelector("#financial-value").textContent = `${100 - weight}%`;
  document.querySelector("#bid-table").innerHTML = currentScores
    .map(
      (b, i) =>
        `<tr><td><strong>${b.name}</strong><span>${b.id}</span></td><td>${b.technical.toFixed(1)}</td><td>${b.financial.toFixed(1)}</td><td class="weighted">${b.weighted.toFixed(1)}</td><td><span class="rank ${i === 0 ? "first" : ""}">${i + 1}</span></td></tr>`,
    )
    .join("");
  document.querySelector("#score-chart").innerHTML = currentScores
    .map(
      (b) =>
        `<div class="score-row" aria-label="${b.name}: ${b.weighted.toFixed(1)} out of 100"><span>${b.name}</span><div class="score-bars" aria-hidden="true"><span class="technical-bar" style="width:${b.technicalContribution}%"></span><span class="financial-bar" style="width:${b.financialContribution}%"></span></div><strong>${b.weighted.toFixed(1)}</strong></div>`,
    )
    .join("");
  document.querySelector("#ai-insight").textContent =
    `At ${weight}% technical and ${100 - weight}% financial weighting, ${currentScores[0].name} leads this sample comparison at ${currentScores[0].weighted.toFixed(1)}/100. Review supporting documents and any compliance exceptions before reaching a decision.`;
  document.querySelector("#rank-summary").textContent =
    `Sample ranking: ${currentScores.map((b, i) => `${i + 1}, ${b.name}, ${b.weighted.toFixed(1)} points`).join("; ")}.`;
}
weightInput.addEventListener("input", updateEvaluation);
updateEvaluation();
document
  .querySelector("#report-open")
  .addEventListener("click", () =>
    openDialog(
      `<h2>Consolidated evaluation report</h2><span class="demo-badge">ILLUSTRATIVE DEMO · SAMPLE DATA</span><p>IT infrastructure tender · Sample comparison only.</p><p>Technical weight: ${weightInput.value}%. Financial weight: ${100 - Number(weightInput.value)}%.</p>${currentScores.map((b, i) => `<div class="report-row"><span>${i + 1}. ${b.name}</span><strong>${b.weighted.toFixed(2)} / 100</strong></div>`).join("")}<p class="note">Sample financial score = lowest bid ÷ supplier bid × 100. Sample bid amounts, in illustrative units: A 124,000; B 115,000; C 138,000. Technical scores are illustrative committee inputs.</p><p class="note">Ollama integration is pending. AI recommendations are advisory; final approval and award remain human committee decisions.</p>`,
    ),
  );

const roles = [
  {
    name: "Admin",
    icon: "shield",
    headline: "A governed foundation.",
    description:
      "Manage the system’s users, role assignments, and configuration so that each participant works within the correct access boundaries.",
    tasks: [
      "Manage user and role configuration",
      "Maintain system settings",
      "Support access governance",
    ],
    stage: "System administration · Across the lifecycle",
  },
  {
    name: "Requesting Department",
    icon: "request",
    headline: "Start with a clear requirement.",
    description:
      "Bring the business need into procurement with a structured purchase request and the supporting information sourcing teams need.",
    tasks: [
      "Prepare purchase requirements",
      "Provide supporting context and documentation",
      "Clarify requirements with the Sourcing Officer",
    ],
    stage: "Purchase Request · Requirement clarification",
  },
  {
    name: "Sourcing Officer",
    icon: "layers",
    headline: "Connect the procurement journey.",
    description:
      "Coordinate tender preparation and supplier participation, keeping procurement information and authorized clarifications in context.",
    tasks: [
      "Prepare and manage tenders",
      "Coordinate supplier bidding",
      "Clarify requirements and evaluation questions",
    ],
    stage: "Create Tender · Supplier Bidding · Clarifications",
  },
  {
    name: "Supplier",
    icon: "people",
    headline: "Participate with confidence.",
    description:
      "Respond to relevant tenders and communicate with the Sourcing Officer within the scope of the supplier’s own participation.",
    tasks: [
      "Prepare and submit bid responses",
      "Provide technical and financial documentation",
      "Answer stage-specific clarification requests",
    ],
    stage: "Supplier Bidding · Bid clarification",
  },
  {
    name: "Evaluation Committee",
    icon: "scales",
    headline: "Evaluate evidence with rigor.",
    description:
      "Assess technical and financial responses against the defined criteria and consolidate the evaluation for authorized review.",
    tasks: [
      "Review technical and financial evidence",
      "Record evaluation scores and observations",
      "Clarify findings with authorized participants",
    ],
    stage: "Evaluation · Technical & Financial",
  },
  {
    name: "Sourcing Manager",
    icon: "check",
    headline: "Bring oversight to the decision.",
    description:
      "Review the consolidated evaluation and coordinate the management approval steps with the relevant committee participants.",
    tasks: [
      "Review evaluation outcomes",
      "Resolve authorized committee clarifications",
      "Coordinate management approval",
    ],
    stage: "Evaluation review · Approvals",
  },
  {
    name: "Top Committee",
    icon: "trophy",
    headline: "Authorize the way forward.",
    description:
      "Review the procurement outcome at the designated approval stage, supported by a consolidated evaluation and documented context.",
    tasks: [
      "Review the submitted approval package",
      "Clarify matters with the Sourcing Manager",
      "Record the authorized committee decision",
    ],
    stage: "Approvals · Award oversight",
  },
];
const roleTabs = document.querySelector(".role-tabs"),
  rolePanel = document.querySelector("#role-panel");
const roleAreas = [
  ["Users & roles", "System configuration", "Access governance"],
  ["Purchase requests", "Supporting documents", "Requirement clarifications"],
  ["Tender management", "Supplier coordination", "Authorized clarifications"],
  [
    "Own bid responses",
    "Own supporting documents",
    "Sourcing Officer clarifications",
  ],
  [
    "Technical & financial evaluation",
    "Scores & observations",
    "Evaluation clarifications",
  ],
  [
    "Consolidated evaluation review",
    "Management approval",
    "Committee clarifications",
  ],
  [
    "Approval package review",
    "Committee decision",
    "Sourcing Manager clarifications",
  ],
];
const roleStages = [[0, 1, 2, 3, 4, 5], [0], [1, 2], [2], [3], [3, 4], [4]];
roleTabs.innerHTML = roles
  .map(
    (r, i) =>
      `<button id="role-tab-${i}" role="tab" aria-selected="${i === 0}" aria-controls="role-panel" tabindex="${i === 0 ? 0 : -1}">${icon(r.icon)}<span>${r.name}</span><b aria-hidden="true">↗</b></button>`,
  )
  .join("");
function selectRole(i, focus = false) {
  const r = roles[i];
  roleTabs.querySelectorAll("button").forEach((b, n) => {
    b.setAttribute("aria-selected", String(n === i));
    b.tabIndex = n === i ? 0 : -1;
    if (n === i && focus) b.focus();
  });
  rolePanel.setAttribute("aria-labelledby", `role-tab-${i}`);
  const authority =
    i === 4
      ? "Committee members assess evidence and record evaluation scores."
      : i === 5
        ? "Manager review is distinct from Evaluation Committee membership and scoring."
        : i === 3
          ? "Participation is limited to the supplier’s own bids and authorized conversations."
          : "Access remains limited to assigned responsibilities and the relevant stage.";
  rolePanel.innerHTML = `<div class="role-detail-header"><span class="role-emblem">${icon(r.icon)}</span><div><span class="micro-label">ROLE PROFILE</span><h3>${r.name}</h3></div><span class="quiet-tag">${i === 4 ? "EVALUATE" : i === 5 ? "REVIEW" : "ROLE-SCOPED"}</span></div><p class="role-summary">${r.description}</p><div class="responsibility-grid">${r.tasks.map((t, n) => `<div>${icon(["request", "check", "chat"][n])}<span>${t}</span></div>`).join("")}</div><div class="role-capabilities"><span class="micro-label">RELEVANT FEATURES & ACCESS SCOPE</span><div>${roleAreas[i].map((a) => `<span>${a}</span>`).join("")}</div></div><div class="role-position"><span class="micro-label">POSITION IN THE PROCUREMENT JOURNEY</span><div class="mini-workflow">${stages.map((s, n) => `<span class="${roleStages[i].includes(n) ? "in-scope" : ""}">${icon(s[1])}<small>${s[0].replace("<br>", " ")}</small></span>`).join("")}</div></div><div class="authority-note">${icon("shield")}<span>${authority}</span></div>`;
  rolePanel.classList.remove("is-changing");
  requestAnimationFrame(() => rolePanel.classList.add("is-changing"));
}
roleTabs.querySelectorAll("button").forEach((b, i) => {
  b.addEventListener("click", () => selectRole(i));
  b.addEventListener("keydown", (e) => {
    let next;
    if (["ArrowDown", "ArrowRight"].includes(e.key))
      next = (i + 1) % roles.length;
    else if (["ArrowUp", "ArrowLeft"].includes(e.key))
      next = (i + roles.length - 1) % roles.length;
    else if (e.key === "Home") next = 0;
    else if (e.key === "End") next = roles.length - 1;
    if (next !== undefined) {
      e.preventDefault();
      selectRole(next, true);
    }
  });
});
selectRole(0);

const relationships = [
  {
    from: "Requesting Department",
    to: "Sourcing Officer",
    short: "Requirement clarification",
    stage: "Purchase Request",
    link: "DEMO-PR-014",
    question:
      "Can the delivery schedule be confirmed for the requested equipment?",
    answer:
      "The sample requirement has been updated with the requested delivery window.",
    file: "Requirement-notes.pdf",
    detail:
      "Illustrative requirement notes: confirm the equipment specification, delivery window, and acceptance criteria before tender preparation.",
  },
  {
    from: "Sourcing Officer",
    to: "Evaluation Committee",
    short: "Evaluation clarification",
    stage: "Evaluation",
    link: "DEMO-TENDER-008",
    question:
      "Please confirm which evidence supports the technical compliance assessment.",
    answer:
      "The assessment references the compliance matrix and supporting technical schedules in the sample bid.",
    file: "Compliance-matrix.xlsx",
    detail:
      "Illustrative compliance matrix: review each criterion against the supplier response and record supporting evidence for the technical score.",
  },
  {
    from: "Evaluation Committee",
    to: "Sourcing Manager",
    short: "Evaluation review",
    stage: "Evaluation review",
    link: "DEMO-TENDER-008",
    question:
      "Please review the consolidated weighting and assessment notes for this sample evaluation.",
    answer:
      "The review notes are recorded. The sample evaluation is ready for the next authorized approval step.",
    file: "Evaluation-summary.pdf",
    detail:
      "Illustrative evaluation summary: technical weight 60%, financial weight 40%. Supporting evidence requires human committee validation.",
  },
  {
    from: "Sourcing Manager",
    to: "Top Committee",
    short: "Approval clarification",
    stage: "Approvals",
    link: "DEMO-TENDER-008",
    question:
      "Is any additional supporting context required for the approval package?",
    answer:
      "Please retain the consolidated evaluation and the documented clarification history with the approval record.",
    file: "Approval-context.pdf",
    detail:
      "Illustrative approval context: consolidate the evaluation summary, decision rationale, and clarification history before committee review.",
  },
  {
    from: "Sourcing Officer",
    to: "Supplier",
    short: "Bid clarification",
    stage: "Supplier Bidding",
    link: "DEMO-BID-001",
    question:
      "Please clarify the support coverage included in your submitted sample proposal.",
    answer:
      "The sample proposal includes the support scope outlined in the attached service schedule.",
    file: "Service-schedule.pdf",
    detail:
      "Illustrative service schedule: provide the support period, service coverage, and response commitments associated with this sample bid.",
  },
];
let relationshipIndex = 0,
  clarificationState = "Answered";
const relationshipList = document.querySelector(".relationship-list");
const communicationIcons = [
  ["request", "layers"],
  ["layers", "scales"],
  ["scales", "check"],
  ["check", "trophy"],
  ["layers", "people"],
];
relationshipList.innerHTML = relationships
  .map(
    (r, i) =>
      `<button aria-pressed="${i === 0}" data-relationship="${i}"><span class="connection-icons">${icon(communicationIcons[i][0])}<b aria-hidden="true">↔</b>${icon(communicationIcons[i][1])}</span><span class="connection-roles"><strong>${r.from}</strong><span aria-hidden="true">↕</span><strong>${r.to}</strong></span><span class="connection-stage">${r.stage}<b aria-hidden="true">↗</b></span></button>`,
  )
  .join("");
function showThread() {
  const r = relationships[relationshipIndex];
  document.querySelector("#thread-participants").innerHTML =
    `<span>${r.from}</span><b aria-label="bidirectional">↔</b><span>${r.to}</span>`;
  document.querySelector("#thread-context").textContent =
    `${r.link} · ${r.stage.toUpperCase()}`;
  document.querySelector("#thread-title").textContent = r.short;
  const status = document.querySelector("#thread-status");
  status.textContent = clarificationState;
  status.dataset.state = clarificationState;
  relationshipList
    .querySelectorAll("button")
    .forEach((b, i) =>
      b.setAttribute("aria-pressed", String(i === relationshipIndex)),
    );
  document
    .querySelectorAll("[data-status]")
    .forEach((b) =>
      b.setAttribute(
        "aria-pressed",
        String(b.dataset.status === clarificationState),
      ),
    );
  document.querySelector(".thread-messages").innerHTML =
    `<article class="message"><span class="avatar" aria-hidden="true">${r.from
      .split(" ")
      .map((x) => x[0])
      .slice(0, 2)
      .join(
        "",
      )}</span><div class="message-body"><div class="message-title"><strong>${r.from}</strong><small>SAMPLE MESSAGE</small></div><p>${r.question}</p><button class="attachment" id="attachment-preview" aria-label="Preview illustrative attachment ${r.file}"><span class="document-thumb">${r.file.endsWith("xlsx") ? "XLS" : "PDF"}</span><span><strong>${r.file}</strong><small>Illustrative attachment · Preview</small></span><b>↗</b></button></div></article>${
      clarificationState === "Pending"
        ? '<p class="thread-awaiting">Awaiting an authorized participant’s response.</p>'
        : `<article class="message"><span class="avatar" aria-hidden="true">${r.to
            .split(" ")
            .map((x) => x[0])
            .slice(0, 2)
            .join(
              "",
            )}</span><div class="message-body"><div class="message-title"><strong>${r.to}</strong><small>SAMPLE RESPONSE</small></div><p>${r.answer}</p></div></article>${clarificationState === "Resolved" ? '<p class="thread-awaiting">✓ Illustrative thread resolved. No further response is pending.</p>' : ""}`
    }`;
  document
    .querySelector("#attachment-preview")
    .addEventListener("click", () =>
      openDialog(
        `<h2>Sample attachment preview</h2><span class="demo-badge">ILLUSTRATIVE CONTENT · NOT AN UPLOADED FILE</span><p><strong>${r.file}</strong></p><p>${r.detail}</p><p class="note">Actual attachment storage, download, and access checks require backend integration.</p>`,
      ),
    );
}
relationshipList.querySelectorAll("button").forEach((b, i) =>
  b.addEventListener("click", () => {
    relationshipIndex = i;
    clarificationState = "Answered";
    showThread();
  }),
);
document.querySelectorAll("[data-status]").forEach((b) =>
  b.addEventListener("click", () => {
    clarificationState = b.dataset.status;
    showThread();
  }),
);
showThread();

// Keep feature cards as useful navigation without changing their reference layout.
const featureDestinations = [
  "roles",
  "evaluation",
  "analytics",
  "clarifications",
];
document.querySelectorAll(".feature").forEach((feature, i) => {
  const h = feature.querySelector("h2"),
    link = document.createElement("a");
  link.href = `#${featureDestinations[i]}`;
  link.textContent = h.textContent;
  link.className = "feature-link";
  h.replaceChildren(link);
});
// The overview CTA opens the narrative directly; authentication actions retain
// explicit unavailable notices until real application routes are provided.
const overviewButton = document.querySelector(".hero .overview");
overviewButton.replaceWith(overviewButton.cloneNode(true));
document
  .querySelector(".hero .overview")
  .addEventListener("click", () =>
    document
      .querySelector("#story")
      .scrollIntoView({ behavior: reduced.matches ? "instant" : "smooth" }),
  );
