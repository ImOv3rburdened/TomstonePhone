const state = {
  token: "",
  profile: null,
};

const profileSummary = document.getElementById("profileSummary");
const authStatus = document.getElementById("authStatus");
const termsText = document.getElementById("termsText");
const privacyText = document.getElementById("privacyText");
const legalTermsVersion = "2026-03-20";
const privacyPolicyVersion = "2026-05-08";

const legalTermsBody = `TomestonePhone User Agreement and Liability Notice

1. You are responsible for the content you send or link through the service.
2. Unlawful, exploitative, abusive, harassing, fraudulent, or illegal sexual material is prohibited.
3. Moderation, review, logging, retention, and enforcement may occur for safety and legal compliance.
4. Removed user-facing content may still be retained for evidentiary and legal purposes.
5. Access may be restricted or terminated for policy or legal risk.
6. To the maximum extent permitted by law, use is at your own risk and no warranty is provided.
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
9. Records may be retained even when user-facing access changes.
10. Relevant data may be disclosed when required for safety response, legal process, or reporting obligations.
11. If you do not agree, do not register or use the service.`;

termsText.textContent = legalTermsBody;
privacyText.textContent = privacyPolicyBody;

document.getElementById("loginButton").addEventListener("click", () => authenticate("/api/auth/login"));
document.getElementById("registerButton").addEventListener("click", () => authenticate("/api/auth/register"));

async function authenticate(endpoint) {
  const username = document.getElementById("username").value.trim();
  const password = document.getElementById("password").value;
  const acceptedTerms = document.getElementById("termsAccepted").checked;
  const acceptedPrivacy = document.getElementById("privacyAccepted").checked;
  const body = endpoint.endsWith("/register")
    ? {
        username,
        password,
        acceptedLegalTerms: acceptedTerms,
        legalTermsVersion,
        acceptedAtUtc: new Date().toISOString(),
        acceptedPrivacyPolicy: acceptedPrivacy,
        privacyPolicyVersion,
        acceptedPrivacyAtUtc: new Date().toISOString(),
      }
    : { username, password };

  if (endpoint.endsWith("/register") && (!acceptedTerms || !acceptedPrivacy)) {
    authStatus.textContent = "You must accept the terms and privacy policy before registering.";
    return;
  }

  const response = await fetch(endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });

  const payload = await response.json();
  if (!response.ok) {
    authStatus.textContent = payload.error || "Authentication failed.";
    return;
  }

  state.token = payload.authToken;
  authStatus.textContent = `Signed in as ${payload.username}`;
  await refreshProfile();
}

async function refreshProfile() {
  if (!state.token) {
    return;
  }

  const response = await fetch("/api/phone/me", {
    headers: { Authorization: `Bearer ${state.token}` },
  });

  if (!response.ok) {
    authStatus.textContent = "Session expired.";
    return;
  }

  const snapshot = await response.json();
  state.profile = snapshot.profile;
  profileSummary.textContent = JSON.stringify({
    username: snapshot.profile.username,
    displayName: snapshot.profile.displayName,
    phoneNumber: snapshot.profile.phoneNumber,
    role: snapshot.profile.role,
    status: snapshot.profile.status,
    notificationsMuted: snapshot.profile.notificationsMuted,
    acceptedLegalTermsVersion: snapshot.profile.acceptedLegalTermsVersion,
    acceptedPrivacyPolicyVersion: snapshot.profile.acceptedPrivacyPolicyVersion,
  }, null, 2);
}
