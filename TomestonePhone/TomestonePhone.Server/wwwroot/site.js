const history = Array(30).fill(0);
let previousBytes = null;
let previousSampleTime = null;
let authToken = "";
const clientVersion = "0.1.15.1";
const legalTermsVersion = "2026-03-20";
const privacyPolicyVersion = "2026-05-08";

const $ = (id) => document.getElementById(id);

const legalTermsBody = `TomestonePhone User Agreement and Liability Notice

1. You are responsible for messages, names, notes, reports, linked media, and any other content you submit through this service.
2. You must not use the service for unlawful, exploitative, abusive, harassing, fraudulent, infringing, or sexually illegal conduct or material.
3. You consent to moderation, logging, retention, review, account restrictions, and disclosure where reasonably necessary for abuse prevention, legal compliance, enforcement, or safety response.
4. User-facing removal does not require backend deletion. Records may be retained for evidentiary, operational, and legal purposes.
5. The operator may suspend, restrict, report, or terminate access for policy violations, security events, or legal risk.
6. This software is provided without warranty. To the maximum extent permitted by law, you assume the risk of use and agree not to hold the operator liable for user-generated conduct except where liability cannot legally be waived.
7. If you do not agree, do not register or use the service.`;

const privacyPolicyBody = `TomestonePhone Privacy Policy

1. TomestonePhone connects to the configured backend server using HTTPS and a DNS hostname. If you use a custom server, your TomestonePhone data goes to that server instead of the maintainer-run server.
2. We collect your TomestonePhone username, password hash, assigned phone number, account role, status, notification settings, and related account metadata to operate the service.
3. If you enable character/world sharing in Settings, the plugin may send your current character name and world to support user-facing display names in chats, calls, contacts, and friends lists. It does not send your Content ID, and accounts are not discoverable by character name.
4. We store messages, chat participation, contacts, friend requests, call records, support tickets, moderation reports, audit logs, and related records to provide features, investigate abuse, and preserve operational and legal records.
5. Voice calls relay live audio packets through the backend while you are in an active call. TomestonePhone does not intentionally record or store call audio.
6. We collect and retain IP addresses and related access data for account security, abuse prevention, moderation, bans, unlawful-content investigations, and legal compliance.
7. The plugin does not collect analytics or telemetry. If analytics are added later, they will require explicit opt-in before collection.
8. The service does not host user-uploaded chat images. If you share external links, related message and moderation records may still be reviewed and retained.
9. We may preserve records, including logs and moderation evidence, even if user-facing access is restricted or content visibility changes.
10. We may disclose relevant information when required for safety response, legal process, or reporting obligations.
11. You may request support, but deletion requests may be limited where retention is necessary for security, legal obligations, or evidentiary purposes.
12. Continued use of the service constitutes ongoing acknowledgement of these practices.`;

$("termsText").textContent = legalTermsBody;
$("privacyText").textContent = privacyPolicyBody;

function selectView(view) {
  const portalActive = view === "portal";
  $("portalView").hidden = !portalActive;
  $("statusView").hidden = portalActive;
  $("portalView").classList.toggle("active", portalActive);
  $("statusView").classList.toggle("active", !portalActive);
  $("portalTab").classList.toggle("active", portalActive);
  $("statusTab").classList.toggle("active", !portalActive);
  $("portalTab").setAttribute("aria-selected", String(portalActive));
  $("statusTab").setAttribute("aria-selected", String(!portalActive));
  history.replaceState(null, "", portalActive ? "#portal" : "#status");
}

$("portalTab").addEventListener("click", () => selectView("portal"));
$("statusTab").addEventListener("click", () => selectView("status"));

async function authenticate(registering) {
  const username = $("username").value.trim();
  const password = $("password").value;
  const acceptedLegalTerms = $("termsAccepted").checked;
  const acceptedPrivacyPolicy = $("privacyAccepted").checked;
  if (!username || !password) {
    $("authStatus").textContent = "Enter both a username and password.";
    return;
  }
  if (registering && (!acceptedLegalTerms || !acceptedPrivacyPolicy)) {
    $("authStatus").textContent = "Accept both policies before creating an account.";
    return;
  }

  $("authStatus").textContent = registering ? "Creating your account…" : "Signing in…";
  const now = new Date().toISOString();
  const body = registering
    ? { username, password, acceptedLegalTerms, legalTermsVersion, acceptedAtUtc: now, acceptedPrivacyPolicy, privacyPolicyVersion, acceptedPrivacyAtUtc: now }
    : { username, password };
  try {
    const response = await fetch(registering ? "/api/auth/register" : "/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-TomestonePhone-Version": clientVersion },
      body: JSON.stringify(body),
    });
    const payload = await response.json();
    if (!response.ok) throw new Error(payload.error || "Authentication failed.");
    authToken = payload.authToken;
    $("authStatus").textContent = `Signed in as ${payload.username}.`;
    await refreshProfile();
  } catch (error) {
    $("authStatus").textContent = error.message || "Authentication failed.";
  }
}

async function refreshProfile() {
  const response = await fetch("/api/phone/me", { headers: { Authorization: `Bearer ${authToken}`, "X-TomestonePhone-Version": clientVersion } });
  if (!response.ok) {
    $("authStatus").textContent = "Your session could not be loaded.";
    return;
  }
  const snapshot = await response.json();
  const profile = snapshot.profile;
  const fields = [
    ["Display name", profile.displayName], ["Username", profile.username], ["Phone number", profile.phoneNumber],
    ["Role", profile.role], ["Account status", profile.status], ["Notifications", profile.notificationsMuted ? "Muted" : "Enabled"],
  ];
  const summary = $("profileSummary");
  summary.replaceChildren();
  fields.forEach(([label, value]) => {
    const dt = document.createElement("dt"); dt.textContent = label;
    const dd = document.createElement("dd"); dd.textContent = value || "—";
    summary.append(dt, dd);
  });
  $("profileEmpty").hidden = true;
  summary.hidden = false;
}

$("loginButton").addEventListener("click", () => authenticate(false));
$("registerButton").addEventListener("click", () => authenticate(true));
$("password").addEventListener("keydown", (event) => { if (event.key === "Enter") authenticate(false); });

function formatRate(bytesPerSecond) {
  if (bytesPerSecond >= 1024 * 1024) return [(bytesPerSecond / 1024 / 1024).toFixed(2), "MB/s"];
  if (bytesPerSecond >= 1024) return [(bytesPerSecond / 1024).toFixed(1), "KB/s"];
  return [Math.round(bytesPerSecond).toLocaleString(), "B/s"];
}

function formatUptime(startedAt) {
  const seconds = Math.max(0, Math.floor((Date.now() - new Date(startedAt).getTime()) / 1000));
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  return days ? `${days}d ${hours}h uptime` : hours ? `${hours}h ${minutes}m uptime` : `${minutes}m uptime`;
}

function updateChart(value) {
  history.push(value);
  history.shift();
  const peak = Math.max(...history, 1024);
  const points = history.map((sample, index) => {
    const x = index * (400 / (history.length - 1));
    const y = 82 - (sample / peak) * 68;
    return [x, y];
  });
  const line = `M${points.map(([x, y]) => `${x.toFixed(1)} ${y.toFixed(1)}`).join("L")}`;
  $("sparkLine").setAttribute("d", line);
  $("sparkArea").setAttribute("d", `${line}L400 90L0 90Z`);
}

function setService(name, operational) {
  const card = document.querySelector(`[data-service="${name}"]`);
  card.classList.toggle("operational", operational);
  card.classList.toggle("degraded", !operational);
  card.querySelector(".service-state").textContent = operational ? "Operational" : "Unavailable";
}

async function refreshStatus() {
  try {
    const response = await fetch("/api/public/status", { cache: "no-store" });
    if (!response.ok) throw new Error(`Status ${response.status}`);
    const data = await response.json();
    const sampledAt = performance.now();
    let bytesPerSecond = 0;
    if (previousBytes !== null && previousSampleTime !== null) {
      const elapsedSeconds = Math.max(0.1, (sampledAt - previousSampleTime) / 1000);
      bytesPerSecond = Math.max(0, (data.totalBytesTransferred - previousBytes) / elapsedSeconds);
    }
    previousBytes = data.totalBytesTransferred;
    previousSampleTime = sampledAt;

    const [rate, unit] = formatRate(bytesPerSecond);
    $("throughputValue").textContent = rate;
    $("throughputUnit").textContent = unit;
    $("onlineMembers").textContent = data.onlineMembers.toLocaleString();
    $("totalMembers").textContent = data.totalMembers.toLocaleString();
    $("voiceConnections").textContent = data.activeVoiceConnections.toLocaleString();
    $("lastUpdated").textContent = `Updated ${new Date(data.generatedAtUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" })}`;
    $("uptime").textContent = formatUptime(data.startedAtUtc);
    updateChart(bytesPerSecond);

    Object.entries(data.services).forEach(([name, operational]) => setService(name, operational));
    const overall = $("overallStatus");
    overall.className = `overall-status ${data.allOperational ? "healthy" : "degraded"}`;
    overall.querySelector("strong").textContent = data.allOperational ? "All systems operational" : "Some systems need attention";
    overall.querySelector("small").textContent = data.allOperational ? "TomestonePhone services are responding normally" : "Live telemetry reports a degraded service";
  } catch {
    const overall = $("overallStatus");
    overall.className = "overall-status degraded";
    overall.querySelector("strong").textContent = "Status service unavailable";
    overall.querySelector("small").textContent = "We could not retrieve live telemetry";
    document.querySelectorAll(".service-card").forEach((card) => {
      card.className = "service-card degraded";
      card.querySelector(".service-state").textContent = "Unknown";
    });
  }
}

refreshStatus();
setInterval(refreshStatus, 1000);
selectView(location.hash === "#status" ? "status" : "portal");
