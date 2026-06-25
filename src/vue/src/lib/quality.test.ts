import { describe, expect, it } from "vitest"
import { Quality } from "./FactorioToolsApi"
import { qualityLevel, QUALITY_ORDER } from "./quality"

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
})
