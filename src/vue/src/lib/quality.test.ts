import { describe, expect, it } from "vitest"
import { Quality } from "./FactorioToolsApi"
import { qualityLevel, qualityPipRadius, qualityPips, QUALITY_ORDER } from "./quality"

describe("quality", () => {
  it("maps quality to its bonus level", () => {
    expect(qualityLevel(Quality.Normal)).toBe(0)
    expect(qualityLevel(Quality.Uncommon)).toBe(1)
    expect(qualityLevel(Quality.Rare)).toBe(2)
    expect(qualityLevel(Quality.Epic)).toBe(3)
    expect(qualityLevel(Quality.Legendary)).toBe(5)
  })

  it("orders qualities from normal to legendary", () => {
    expect(QUALITY_ORDER).toEqual([
      Quality.Normal,
      Quality.Uncommon,
      Quality.Rare,
      Quality.Epic,
      Quality.Legendary,
    ])
  })

  it("uses the in-game pip count for each quality glyph", () => {
    expect(qualityPips(Quality.Normal)).toHaveLength(1)
    expect(qualityPips(Quality.Uncommon)).toHaveLength(2)
    expect(qualityPips(Quality.Rare)).toHaveLength(3)
    expect(qualityPips(Quality.Epic)).toHaveLength(4)
    expect(qualityPips(Quality.Legendary)).toHaveLength(5)
  })

  it("keeps every pip within the 0-24 glyph viewBox", () => {
    for (const quality of QUALITY_ORDER) {
      const radius = qualityPipRadius(quality)
      for (const [cx, cy] of qualityPips(quality)) {
        expect(cx - radius).toBeGreaterThanOrEqual(0)
        expect(cx + radius).toBeLessThanOrEqual(24)
        expect(cy - radius).toBeGreaterThanOrEqual(0)
        expect(cy + radius).toBeLessThanOrEqual(24)
      }
    }
  })
})
