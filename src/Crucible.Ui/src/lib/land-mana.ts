import type { Card } from "./types.ts";
import { normalizeMana, manaSegments, symbolLabel } from "./mana.ts";

const manaTypes = "WUBRGC";
const basicTypes = ["Plains", "Island", "Swamp", "Mountain", "Forest"];
const names = ["white", "blue", "black", "red", "green", "colorless"];
export interface LandProduction {
  symbols: string[];
  conditional: boolean;
  status: "produces" | "finds-land" | "none" | "unknown";
  description: string;
}

// Reminder text and quoted abilities granted to other objects are not this
// land's output. Basic land types supply their intrinsic output separately.
function ownRules(text: string): string {
  let rules = text.replace(/"[^"\n]*"|“[^”\n]*”/g, "");
  while (/\([^()]*\)/.test(rules)) rules = rules.replace(/\([^()]*\)/g, "");
  return rules;
}

export function landProduction(
  card: Pick<Card, "type" | "text">,
): LandProduction | null {
  const frontType = card.type.split("//", 1)[0];
  const [types, subtypes = ""] = frontType.split(/[—–]/, 2);
  if (!/\bLand\b/i.test(types)) return null;
  const text = normalizeMana(
    (card.text ?? "").split("//", 1)[0].replace(/\\n/g, "\n"),
  );
  const rules = ownRules(text);
  const outputs = new Set<string>();
  for (const [i, type] of basicTypes.entries()) {
    if (new RegExp(`\\b${type}\\b`, "i").test(subtypes))
      outputs.add(manaTypes[i]);
  }
  let conditional = false;
  let unresolved = false;
  const chosenType =
    /choose a basic land type/i.test(rules) &&
    /this land is the chosen type/i.test(rules);
  if (chosenType) {
    for (const symbol of "WUBRG") outputs.add(symbol);
    conditional = true;
  }
  for (const rawLine of rules.split("\n")) {
    // Arena serializes station-gated abilities alongside their rules text.
    const station = rawLine.match(
      /^STATIONLEVEL\s+\[[^\]]*\]\s+\[[^\]]*\]\s+\[(.*)\]\s*$/i,
    );
    const line = station?.[1] ?? rawLine;
    if (!/\badd\b/i.test(line)) continue;
    if (station) conditional = true;
    const ability = line.match(/^\s*([^:]+):\s*Add\s+(.+)$/i);
    // Interpret this land's explicit activated mana abilities. Unrecognized
    // production stays unknown rather than guessing from arbitrary symbols.
    if (
      !ability ||
      !/^\s*(?:\{|Pay\b|Discard\b|Sacrifice\b|Remove\b|Exile\b|Tap\b|Untap\b)/i.test(
        ability[1],
      )
    ) {
      unresolved = true;
      continue;
    }
    const [, cost, effect] = ability;
    if ((line.match(/\badd\b/gi)?.length ?? 0) > 1) unresolved = true;
    const output = effect.split(
      /[.!?]|\b(?:then|if|unless|for each|equal to|where|spend|activate|you may)\b/i,
      1,
    )[0];
    const symbols = [...output.matchAll(/\{([WUBRGC])\}/gi)].map((m) =>
      m[1].toUpperCase(),
    );
    if (/\bany (?:one )?color\b|\bany combination of colors\b/i.test(output))
      symbols.push(..."WUBRG");
    if (/\bany (?:one )?type\b/i.test(output)) symbols.push(...manaTypes);
    if (!symbols.length) unresolved = true;
    for (const symbol of symbols) outputs.add(symbol);
    // {T} alone is the ordinary activation; filtering, life, sacrifices,
    // spending restrictions, and state-dependent output need explanation.
    if (
      cost.replace(/\{T\}|[\s,]/gi, "") ||
      /\b(?:if|unless|only|for each|equal to|chosen|could produce|this way|spend|activate)\b/i.test(
        effect,
      )
    )
      conditional = true;
  }
  const symbols = [...manaTypes].filter((symbol) => outputs.has(symbol));
  const readableRules = manaSegments(text.replace(/^STATIONLEVEL.*$/gim, ""))
    .map((s) => (s.kind === "symbol" ? symbolLabel(s.value) : s.value))
    .join("");
  if (symbols.length) {
    const colors = symbols.map((symbol) => names[manaTypes.indexOf(symbol)]);
    const label = "WUBRG".split("").every((symbol) => outputs.has(symbol))
      ? `Can produce any color${outputs.has("C") ? " or colorless" : ""} mana.`
      : `${symbols.length === 1 ? "Produces" : "Can produce"} ${colors.join(" or ")} mana.`;
    return {
      symbols,
      conditional,
      status: unresolved ? "unknown" : "produces",
      description: [
        label,
        conditional
          ? "Conditional: some output requires an additional cost, choice, or restriction."
          : "",
        unresolved ? "Additional mana production unknown." : "",
        conditional ? readableRules : "",
      ]
        .filter(Boolean)
        .join(" "),
    };
  }
  const status =
    unresolved ||
    !text.trim() ||
    /^(?:unknown|unavailable|not available|\?)$/i.test(text.trim()) ||
    /\b(?:copy|mana of|mana type|this land (?:has|gains?))\b/i.test(rules)
      ? "unknown"
      : /\bsearch your library for\b[^.\n]*\bland\b/i.test(rules)
        ? "finds-land"
        : "none";
  return {
    symbols,
    conditional,
    status,
    description:
      status === "finds-land"
        ? "Finds a land; does not directly produce mana."
        : status === "none"
          ? "No mana production."
          : "Mana production unknown. Expand the card rules for details.",
  };
}
