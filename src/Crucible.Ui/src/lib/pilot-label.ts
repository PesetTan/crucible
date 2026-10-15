import type { Catalog } from "./types.ts";

// Display only: profile IDs and recorded assignments remain unchanged.
export function pilotLabel(
  profile: string | undefined,
  catalog: Pick<Catalog, "pilotNames" | "pilotLabels"> | undefined,
  detailed = false,
): string {
  const name = (detailed ? catalog?.pilotLabels[profile ?? ""] : undefined)
    ?? catalog?.pilotNames[profile ?? ""]
    ?? profile
    ?? "Not recorded";
  return `Pilot: ${name.replace(/^(?:Pilot:\s*)+/i, "")}`;
}
