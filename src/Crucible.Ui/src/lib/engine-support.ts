import type { Coverage, Deck, EngineSupport } from "./types.ts";
import { api, ApiError } from "./api.ts";

export function engineLabel(provider: string, version?: string) {
  const name =
    provider === "pyre" ? "Pyre" : provider === "forge" ? "Forge" : provider;
  return version ? `${name} ${version}` : name;
}

export async function loadEngineSupport(): Promise<EngineSupport[]> {
  try {
    return await api<EngineSupport[]>("/api/v1/engine-support");
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return [];
    throw e;
  }
}

export function forgeSupport(coverage?: Coverage) {
  if (coverage?.status === "blocked")
    return {
      label: "Unsupported",
      kind: "blocked",
      reason: "Required Forge card scripts are missing.",
    };
  if (coverage?.status === "exploratory")
    return {
      label: "Exploratory",
      kind: "limited",
      reason:
        "Forge card scripts are present; rules interactions and pilot quality remain unvalidated.",
    };
  return {
    label: "Coverage unavailable",
    kind: "unknown",
    reason: "Forge coverage has not been checked for this list.",
  };
}

export function supportsSelection(decks: Deck[], catalog: EngineSupport) {
  if (decks.length < 2 || decks.some((d) => d.sideCount !== 0)) return false;
  const accepted = decks.map(
    (d) =>
      new Set(
        catalog.decks
          .filter((e) => e.listHash === d.listHash && e.sideCount === 0)
          .map((e) => e.id),
      ),
  );
  return accepted.every((a, i) =>
    accepted
      .slice(i + 1)
      .every((b) =>
        catalog.matchups.some(
          (m) =>
            (a.has(m.deckA) && b.has(m.deckB)) ||
            (a.has(m.deckB) && b.has(m.deckA)),
        ),
      ),
  );
}

export function supportedMatchups(deck: Deck, catalog: EngineSupport) {
  if (deck.sideCount !== 0) return [];
  const ids = new Set(
    catalog.decks
      .filter((d) => d.listHash === deck.listHash && d.sideCount === 0)
      .map((d) => d.id),
  );
  return catalog.matchups.filter((m) => ids.has(m.deckA) || ids.has(m.deckB));
}

export function supportedCards(
  catalogs: EngineSupport[],
): Record<string, string[]> {
  const cards: Record<string, string[]> = Object.create(null);
  for (const catalog of catalogs)
    for (const card of catalog.cards) {
      if (card.kind !== "Card") continue;
      (cards[card.name] ??= []).push(
        engineLabel(catalog.providerId, catalog.engineVersion),
      );
    }
  return cards;
}
