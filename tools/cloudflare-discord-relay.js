/**
 * CabSpan — Cloudflare Worker Discord Webhook Relay
 *
 * Purpose:
 *   Allows public GitHub builds of CabSpan.exe to send rich Discord crash embeds and feature suggestions
 *   through a rate-limited Cloudflare Worker URL (`https://cabspan-telemetry.itsjuztin.workers.dev`)
 *   while keeping your actual Discord Webhook URLs hidden inside Cloudflare encrypted secrets.
 *
 * Quick Setup (2 Minutes):
 *   1. Go to https://dash.cloudflare.com -> Workers & Pages -> Create Worker (`cabspan-telemetry`).
 *   2. Paste this script and click Deploy.
 *   3. In Worker Settings -> Variables and Secrets, add two Secrets (from your %AppData%\CabSpan\webhooks.local.json):
 *      - Name: DISCORD_WEBHOOK_URL              Value: https://discord.com/api/webhooks/... (Diagnostics/Bugs channel)
 *      - Name: DISCORD_SUGGESTIONS_WEBHOOK_URL  Value: https://discord.com/api/webhooks/... (Suggestions channel)
 */

const RATE_LIMIT_WINDOW_MS = 15 * 60 * 1000; // 15 minutes
const MAX_REQUESTS_PER_WINDOW = 5;
const MAX_PAYLOAD_BYTES = 8192;
const ipHits = new Map();

function isRateLimited(ip) {
  const now = Date.now();
  const timestamps = (ipHits.get(ip) || []).filter(t => now - t < RATE_LIMIT_WINDOW_MS);
  if (timestamps.length >= MAX_REQUESTS_PER_WINDOW) {
    ipHits.set(ip, timestamps);
    return true;
  }
  timestamps.push(now);
  ipHits.set(ip, timestamps);
  return false;
}

export default {
  async fetch(request, env) {
    if (request.method !== "POST") {
      return new Response("CabSpan Telemetry Relay Active", { status: 200 });
    }

    const clientIp = request.headers.get("CF-Connecting-IP") || "unknown";
    if (isRateLimited(clientIp)) {
      return new Response("Rate limit exceeded. Please wait before submitting again.", { status: 429 });
    }

    const contentLength = Number(request.headers.get("Content-Length") || 0);
    if (contentLength > MAX_PAYLOAD_BYTES) {
      return new Response("Payload too large", { status: 413 });
    }

    const url = new URL(request.url);
    const isSuggestionRoute = url.pathname.endsWith("/suggestions") || url.searchParams.get("channel") === "suggestions";
    const targetWebhook = isSuggestionRoute
      ? (env.DISCORD_SUGGESTIONS_WEBHOOK_URL || env.DISCORD_WEBHOOK_URL)
      : env.DISCORD_WEBHOOK_URL;

    if (!targetWebhook) {
      return new Response("Missing Discord webhook secret", { status: 500 });
    }

    try {
      const rawBody = await request.text();
      if (rawBody.length > MAX_PAYLOAD_BYTES) {
        return new Response("Payload too large", { status: 413 });
      }

      const payload = JSON.parse(rawBody);
      if (!payload || !Array.isArray(payload.embeds) || payload.embeds.length === 0) {
        return new Response("Invalid embed payload", { status: 400 });
      }

      const safeUsername = isSuggestionRoute ? "CabSpan Feature Suggestions" : (payload.username || "CabSpan Diagnostics");
      const discordResponse = await fetch(targetWebhook, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          username: String(safeUsername).slice(0, 64),
          embeds: payload.embeds.slice(0, 1)
        })
      });

      return new Response("OK", { status: discordResponse.status });
    } catch {
      return new Response("Bad Request", { status: 400 });
    }
  }
};
