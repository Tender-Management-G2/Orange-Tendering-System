"use strict";
const stages = [
  ["Purchase<br>Request", "request", 13.9, 0.1],
  ["Create<br>Tender", "layers", 29.15, 0.1],
  ["Supplier<br>Bidding", "people", 44, -0.65],
  ["Evaluation", "scales", 58.55, -2.25, "(Technical & Financial)"],
  ["Approvals", "check", 73.98, -2.05, "(Manager & Committee)"],
  ["Award<br>& Closure", "trophy", 89.6, -2.05],
];
const features = [
  ["Secure & Role-Based", "Controlled access for all stakeholders", "shield"],
  ["AI Bid Analysis", "Intelligent comparison with Ollama", "ai"],
  ["Real-Time Tracking", "Follow the tender lifecycle end-to-end", "bars"],
  [
    "Clarifications & Inquiries",
    "Structured, role-based communication",
    "chat",
  ],
];
const icon = (id) => `<svg aria-hidden="true"><use href="#${id}"/></svg>`;
document.querySelector(".stage-list").innerHTML = stages
  .map(
    ([title, id, left, top, subtitle]) =>
      `<article class="stage" style="--left:${left};--top:${top}" aria-label="${title.replace("<br>", " ")}">${icon(id)}<h3>${title}</h3>${subtitle ? `<small>${subtitle}</small>` : ""}</article>`,
  )
  .join("");
document.querySelector(".features").innerHTML = features
  .map(
    ([title, body, id]) =>
      `<article class="feature">${icon(id)}<div><h2>${title}</h2><p>${body}</p></div></article>`,
  )
  .join("");
const route = document.querySelector("#route"),
  track = document.querySelector(".track"),
  cards = [...document.querySelectorAll(".stage")],
  nodes = document.querySelector(".nodes"),
  pulse = document.querySelector(".pulse"),
  halo = document.querySelector(".pulse-halo"),
  tail = document.querySelector(".pulse-tail");
const reduced = matchMedia("(prefers-reduced-motion: reduce)"),
  mobile = matchMedia("(max-width: 900px)");
let length,
  positions = [],
  paused = false,
  elapsed = 0,
  last = 0,
  raf = 0;
function layout() {
  const box = track.getBoundingClientRect(),
    width = box.width,
    height = box.height;
  if (!width || !height) return;
  track.setAttribute("viewBox", `0 0 ${width} ${height}`);
  const targets = cards.map((card) => {
    const r = card.getBoundingClientRect();
    return [r.left - box.left + r.width / 2, r.bottom - box.top + 17];
  });
  let path = `M-25 ${targets[0][1] - 45} C30 ${targets[0][1] - 30} ${targets[0][0] - 45} ${targets[0][1] + 12} ${targets[0][0]} ${targets[0][1]}`;
  for (let i = 1; i < targets.length; i++) {
    const [x, y] = targets[i],
      [px, py] = targets[i - 1],
      dx = x - px;
    if (mobile.matches && i % 2 === 0) {
      const midY = py + 29;
      path += ` C${width - 8} ${py} ${width - 8} ${midY} ${width - 42} ${midY} L42 ${midY} C8 ${midY} 8 ${y} ${x} ${y}`;
    } else {
      const bend = i % 2 === 0 ? 16 : -19;
      path += ` C${px + dx * 0.35} ${py + bend} ${x - dx * 0.35} ${y + bend} ${x} ${y}`;
    }
  }
  const end = targets[5];
  path += ` C${end[0] + 55} ${end[1] + 12} ${width - 10} ${end[1] + 22} ${width + 25} ${end[1] - 8}`;
  route.setAttribute("d", path);
  tail.setAttribute("d", path);
  length = route.getTotalLength();
  positions = targets.map(([x, y]) => {
    let best = 0,
      dist = Infinity;
    for (let i = 0; i <= 1800; i++) {
      const d = (length * i) / 1800,
        p = route.getPointAtLength(d),
        v = (p.x - x) ** 2 + (p.y - y) ** 2;
      if (v < dist) {
        dist = v;
        best = d;
      }
    }
    return best;
  });
  nodes.innerHTML = positions
    .map((d, i) => {
      let p = route.getPointAtLength(d);
      return `<g transform="translate(${p.x} ${p.y})"><circle class="node-outer" r="${i === 5 ? 16 : 9}"/>${i === 5 ? '<path d="m-7 0 4 4 9-10" stroke="white" stroke-width="2.5"/>' : '<circle class="node-inner" r="5"/>'}</g>`;
    })
    .join("");
  render(elapsed);
}
function render(time) {
  if (!length) return;
  const progress = (time % 7200) / 7200,
    d = progress * length,
    p = route.getPointAtLength(d);
  pulse.setAttribute("cx", p.x);
  pulse.setAttribute("cy", p.y);
  halo.setAttribute("cx", p.x);
  halo.setAttribute("cy", p.y);
  const trail = Math.min(length * 0.09, d);
  tail.style.strokeDasharray = `${trail} ${length + 1}`;
  tail.style.strokeDashoffset = -(d - trail);
  const fade = Math.min(1, progress * 30, (1 - progress) * 30);
  pulse.style.opacity =
    halo.style.opacity =
    tail.style.opacity =
      reduced.matches ? 0 : fade;
  positions.forEach((pos, i) => {
    const delta = d - pos,
      width = length * (mobile.matches ? 0.027 : 0.038),
      energy = reduced.matches
        ? i === 3
          ? 0.65
          : 0
        : Math.exp(-((delta / (delta > 0 ? width * 1.2 : width * 0.7)) ** 2)) *
          (i === 3 ? 1.18 : 1);
    cards[i].style.setProperty("--energy", energy.toFixed(3));
    const node = nodes.children[i];
    node.style.filter = `drop-shadow(0 0 ${4 + energy * 13}px #ff7900)`;
    node.style.opacity = 0.8 + Math.min(energy, 1) * 0.2;
    if (i === 5) {
      node.querySelector(".node-outer").setAttribute("r", 16 + energy * 3);
      document
        .querySelector(".workflow")
        .style.setProperty("--completion", energy.toFixed(3));
    }
  });
}
function tick(now) {
  if (!last) last = now;
  const dt = Math.min(now - last, 80);
  last = now;
  if (!paused && !reduced.matches && !document.hidden) elapsed += dt;
  render(elapsed);
  raf = requestAnimationFrame(tick);
}
layout();
if (!reduced.matches) raf = requestAnimationFrame(tick);
let resizePending = false;
new ResizeObserver(() => {
  if (!resizePending) {
    resizePending = true;
    requestAnimationFrame(() => {
      cards.forEach((c) => c.style.setProperty("--energy", 0));
      layout();
      resizePending = false;
    });
  }
}).observe(document.querySelector(".workflow"));
mobile.addEventListener("change", layout);
reduced.addEventListener("change", () => {
  cancelAnimationFrame(raf);
  last = 0;
  render(elapsed);
  if (!reduced.matches) raf = requestAnimationFrame(tick);
});
document.addEventListener("visibilitychange", () => {
  last = 0;
});
const dots = [...document.querySelectorAll(".pagination button")];
dots.forEach((b, i) =>
  b.addEventListener("click", () => {
    paused = i === 1;
    if (i === 2) elapsed = 0;
    dots.forEach((x) => x.classList.remove("active"));
    dots[i === 2 ? 0 : i].classList.add("active");
    render(elapsed);
  }),
);
const observer = new IntersectionObserver(
  (entries) =>
    entries.forEach((e) => {
      if (e.isIntersecting) {
        e.target.classList.add("visible");
        observer.unobserve(e.target);
      }
    }),
  { threshold: 0.1 },
);
document.querySelectorAll(".reveal").forEach((x) => observer.observe(x));
let scrollQueued = false;
window.addEventListener(
  "scroll",
  () => {
    if (!reduced.matches && !scrollQueued) {
      scrollQueued = true;
      requestAnimationFrame(() => {
        document.documentElement.style.setProperty(
          "--parallax",
          `${Math.min(scrollY * 0.12, 55)}px`,
        );
        scrollQueued = false;
      });
    }
  },
  { passive: true },
);
const dialog = document.querySelector("dialog"),
  content = document.querySelector(".dialog-content");
const views = {
  overview: `<span class="eyebrow">HOW IT WORKS</span><h2>A connected tender lifecycle</h2><p>Follow the glowing path through each stage of the procurement journey.</p><ol>${stages.map((s) => `<li>${s[0].replace("<br>", " ")} ${s[4] || ""}</li>`).join("")}</ol><p class="note">The homepage animation illustrates the workflow. Use the dots beneath it to play, pause, or restart.</p>`,
  signin:
    '<h2>Sign In</h2><p>Access your sourcing and tender workspace.</p><p class="status">This homepage is ready to connect to your organization’s authentication service. Account sign-in is not configured in this preview.</p>',
  register:
    '<h2>Supplier Registration</h2><p>Build a trusted supplier partnership with Orange.</p><p class="status">Supplier onboarding is not connected in this homepage preview. Your procurement administrator can connect this action to the approved registration portal.</p>',
  contact:
    '<h2>Contact procurement</h2><p class="status">Contact details have not been supplied. Your procurement team can add their approved support channel here.</p>',
};
function openDialog(html) {
  content.innerHTML = html.replace("<h2>", '<h2 id="dialog-title">');
  dialog.showModal();
}
document
  .querySelectorAll("[data-dialog]")
  .forEach((b) =>
    b.addEventListener("click", () => openDialog(views[b.dataset.dialog])),
  );
document
  .querySelector(".close")
  .addEventListener("click", () => dialog.close());
dialog.addEventListener("click", (e) => {
  if (e.target === dialog) {
    const r = dialog.getBoundingClientRect();
    if (
      e.clientX < r.left ||
      e.clientX > r.right ||
      e.clientY < r.top ||
      e.clientY > r.bottom
    )
      dialog.close();
  }
});
