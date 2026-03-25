const state = {
  token: "",
  profile: null,
};

const profileSummary = document.getElementById("profileSummary");
const authStatus = document.getElementById("authStatus");
const termsText = document.getElementById("termsText");
const privacyText = document.getElementById("privacyText");
const legalTermsVersion = "2026-03-20";
const privacyPolicyVersion = "2026-03-20";

const legalTermsBody = `TomestonePhone User Agreement and Liability Notice

1. You are responsible for the content you send or link through the service.
2. Unlawful, exploitative, abusive, harassing, fraudulent, or illegal sexual material is prohibited.
3. Moderation, review, logging, retention, and enforcement may occur for safety and legal compliance.
4. Removed user-facing content may still be retained for evidentiary and legal purposes.
5. Access may be restricted or terminated for policy or legal risk.
6. To the maximum extent permitted by law, use is at your own risk and no warranty is provided.
7. If you do not agree, do not register or use the service.`;

const privacyPolicyBody = `TomestonePhone Privacy Policy

1. We collect your username, password hash, assigned phone number, role, and account status to run the service.
2. We store messages, support tickets, moderation reports, and audit logs to deliver features, investigate abuse, and preserve records.
3. We log IP addresses for account security, abuse prevention, bans, unlawful-content investigations, and legal compliance.
4. User-hosted chat image uploads are disabled. External links shared in messages may still be reviewed through message and moderation records.
5. Records may be retained even when user-facing access changes.
6. Relevant data may be disclosed when required for safety response, legal process, or reporting obligations.
7. If you do not agree, do not register or use the service.`;

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
