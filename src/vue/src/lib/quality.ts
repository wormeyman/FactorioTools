import { Quality } from "./FactorioToolsApi"

export const QUALITY_ORDER: Quality[] = [
  Quality.Normal,
  Quality.Uncommon,
  Quality.Rare,
  Quality.Epic,
  Quality.Legendary,
]

const levels: Record<Quality, number> = {
  [Quality.Normal]: 0,
  [Quality.Uncommon]: 1,
  [Quality.Rare]: 2,
  [Quality.Epic]: 3,
  [Quality.Legendary]: 5,
}

const labels: Record<Quality, string> = {
  [Quality.Normal]: "Normal",
  [Quality.Uncommon]: "Uncommon",
  [Quality.Rare]: "Rare",
  [Quality.Epic]: "Epic",
  [Quality.Legendary]: "Legendary",
}

// Official Factorio quality colors (approximate; confirm against the game/wiki).
const colors: Record<Quality, string> = {
  [Quality.Normal]: "#c8c8c8",
  [Quality.Uncommon]: "#4fd24f",
  [Quality.Rare]: "#3f9bff",
  [Quality.Epic]: "#b34dff",
  [Quality.Legendary]: "#ff912d",
}

export function qualityLevel(quality: Quality): number {
  return levels[quality]
}

export function qualityLabel(quality: Quality): string {
  return labels[quality]
}

export function qualityColor(quality: Quality): string {
  return colors[quality]
}
