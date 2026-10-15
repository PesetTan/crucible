// Preserve signed 64-bit seeds/sequence identities without rounding them through JS Number.
export function parseExact<T>(raw: string): T {
  const protectedJson = raw.replace(
    /"(?:\\.|[^"\\])*"|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
    (token, integer: string | undefined) =>
      integer &&
      /^-?\d+$/.test(integer) &&
      !Number.isSafeInteger(Number(integer))
        ? JSON.stringify(integer)
        : token,
  );
  return JSON.parse(protectedJson) as T;
}
export class ApiError extends Error {
  status: number;
  constructor(message: string, status: number) {
    super(message);
    this.status = status;
  }
}
const cache = new Map<string, { tag: string; value: unknown; bytes: number }>();
let cachedBytes = 0,
  generation = 0;
const cacheLimit = 8 * 1024 * 1024;
let session: Promise<string> | undefined;
async function instanceToken() {
  return (session ??= fetch("/api/v1/session", { cache: "no-store" })
    .then(async (r) => {
      if (!r.ok) throw new ApiError("Local UI is unavailable.", r.status);
      return (await r.json()).instanceToken as string;
    })
    .catch((e) => {
      session = undefined;
      throw e;
    }));
}
export async function api<T>(
  path: string,
  options: {
    method?: string;
    body?: unknown;
    signal?: AbortSignal;
    fresh?: boolean;
    key?: string;
  } = {},
): Promise<T> {
  if (!path.startsWith("/api/v1/"))
    throw new Error("Only the local versioned API is allowed.");
  const startedGeneration = generation;
  const method = options.method ?? "GET";
  const headers: Record<string, string> = {};
  const saved = cache.get(path);
  if (method === "GET" && saved && !options.fresh)
    headers["If-None-Match"] = saved.tag;
  if (method !== "GET") {
    headers["X-Instance-Token"] = await instanceToken();
    headers["Content-Type"] = "application/json";
    if (options.key) headers["Idempotency-Key"] = options.key;
  }
  const send = () =>
    fetch(path, {
      method,
      headers,
      signal: options.signal,
      cache: "no-cache",
      body:
        options.body === undefined ? undefined : JSON.stringify(options.body),
    });
  let response = await send();
  if (response.status === 403 && method !== "GET") {
    session = undefined;
    headers["X-Instance-Token"] = await instanceToken();
    response = await send();
  }
  if (response.status === 304 && saved) return saved.value as T;
  const raw = await response.text();
  if (!response.ok) {
    let message = raw;
    try {
      message = JSON.parse(raw).message ?? raw;
    } catch {}
    throw new ApiError(
      message || `Request failed (${response.status}).`,
      response.status,
    );
  }
  const value = raw ? parseExact<T>(raw) : (undefined as T);
  const tag = response.headers.get("etag");
  if (
    method === "GET" &&
    tag &&
    generation === startedGeneration &&
    raw.length <= cacheLimit / 2
  ) {
    const previous = cache.get(path);
    if (previous) {
      cachedBytes -= previous.bytes;
      cache.delete(path);
    }
    while (
      cache.size &&
      (cache.size >= 6 || cachedBytes + raw.length > cacheLimit)
    ) {
      const key = cache.keys().next().value!;
      cachedBytes -= cache.get(key)!.bytes;
      cache.delete(key);
    }
    cache.set(path, { tag, value, bytes: raw.length });
    cachedBytes += raw.length;
  }
  if (method !== "GET") clearCache();
  return value;
}
export function idempotencyKey(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (n) => n.toString(16).padStart(2, "0")).join("");
}
export function clearCache() {
  cache.clear();
  cachedBytes = 0;
  generation++;
}
// No overlapping requests; stop polling in background windows, retry only when visible.
export function poll(
  task: () => Promise<void>,
  interval: () => number = () => 10000,
) {
  let stopped = false,
    running = false,
    timer: ReturnType<typeof setTimeout> | undefined;
  async function tick() {
    if (stopped || running) return;
    running = true;
    try {
      if (!document.hidden) await task();
    } catch {
    } finally {
      running = false;
      if (!stopped) timer = setTimeout(tick, interval());
    }
  }
  function visible() {
    if (!document.hidden) {
      if (timer) clearTimeout(timer);
      void tick();
    }
  }
  document.addEventListener("visibilitychange", visible);
  timer = setTimeout(tick, interval());
  return () => {
    stopped = true;
    if (timer) clearTimeout(timer);
    document.removeEventListener("visibilitychange", visible);
  };
}
export function loadSaved<T>(key: string, fallback: T): T {
  try {
    const current = localStorage.getItem("crucible:svelte:" + key);
    if (current !== null) return parseExact<T>(current) ?? fallback;
    if (key === "home") {
      const old = parseExact<Record<string, unknown>>(
        localStorage.getItem("gauntlet:selections:v1:home") ?? "null",
      );
      if (old)
        return {
          ...fallback,
          selected: old.ExplicitDeckSelection ? (old.DeckIds ?? []) : [],
          games: old.Games,
          workers: old.Workers,
          timeout: old.Timeout,
          turnLimit: old.TurnLimit,
          seed: String(old.Seed ?? 42),
          performance: old.PerformanceMode ?? false,
          pilot: old.PilotMode ?? "",
        };
    }
    if (key.startsWith("result:")) {
      const old = parseExact<Record<string, unknown>>(
        localStorage.getItem(
          "gauntlet:selections:v1:results:" + key.slice(7),
        ) ?? "null",
      );
      if (old)
        return {
          ...fallback,
          pair: old.Matchup ?? "",
          unit: old.TimeUnit ?? "rounds",
        };
    }
    if (key === "replay-audit") {
      const old = parseExact<{ Audit: boolean }>(
        localStorage.getItem("gauntlet:selections:v1:replay") ?? "null",
      );
      if (old) return old.Audit as T;
    }
    if (key.startsWith("workshop:")) {
      const old = parseExact<Record<string, unknown>>(
        localStorage.getItem("gauntlet:selections:v1:" + key) ?? "null",
      );
      const cards = (value: unknown) =>
        Array.isArray(value)
          ? value.map((card) =>
              Object.fromEntries(
                Object.entries(card).map(([k, v]) => [
                  k[0].toLowerCase() + k.slice(1),
                  v,
                ]),
              ),
            )
          : [];
      if (old)
        return {
          ...fallback,
          name: old.Name,
          parentId: old.ParentId ?? "",
          savedId: old.SavedId ?? "",
          cards: cards(old.Cards),
          sideboard: cards(old.Sideboard),
          opponent: old.OpponentId ?? "",
          baseline: old.BaselineRunId ?? "",
          baselineDeck: old.BaselineDeckId ?? "",
          latest: old.LatestRunId ?? "",
          latestDeckId: old.LatestDeckId ?? "",
          games: old.Games,
          workers: old.Workers,
          timeout: old.Timeout,
          turnLimit: old.TurnLimit,
          seed: String(old.Seed ?? 42),
          performance: old.Performance ?? false,
          stagger: old.Stagger ?? true,
          spacing: old.Spacing === null ? null : (old.Spacing ?? 1),
          estimatedSeconds: old.EstimatedSeconds ?? 15,
          leadSeconds: old.LeadSeconds ?? 5,
        };
    }
    return fallback;
  } catch {
    return fallback;
  }
}
export function save(key: string, value: unknown) {
  try {
    localStorage.setItem("crucible:svelte:" + key, JSON.stringify(value));
  } catch {}
}
