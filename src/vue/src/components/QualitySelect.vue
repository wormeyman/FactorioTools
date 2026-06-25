<template>
  <div class="row" v-show="showAdvancedOptions">
    <div class="col">
      <label :for="idPrefix + '-quality'" class="form-label d-flex align-items-center gap-2">
        <span class="quality-badge" :style="{ backgroundColor: badgeColor }"></span>
        {{ label }}
      </label>
      <select
        class="form-select"
        :id="idPrefix + '-quality'"
        :value="modelValue"
        @change="$emit('update:modelValue', ($event.target as HTMLSelectElement).value)"
      >
        <option v-for="q in order" :key="q" :value="q">{{ qualityLabel(q) }}</option>
      </select>
    </div>
  </div>
</template>

<script lang="ts">
import { Quality } from "../lib/FactorioToolsApi"
import { QUALITY_ORDER, qualityColor, qualityLabel } from "../lib/quality"

export default {
  props: {
    showAdvancedOptions: { type: Boolean, required: true },
    label: { type: String, required: true },
    idPrefix: { type: String, required: true },
    modelValue: { type: String, required: true },
  },
  emits: ["update:modelValue"],
  computed: {
    order() {
      return QUALITY_ORDER
    },
    badgeColor(): string {
      return qualityColor(this.modelValue as Quality)
    },
  },
  methods: {
    qualityLabel,
  },
}
</script>

<style scoped>
.quality-badge {
  display: inline-block;
  width: 0.85rem;
  height: 0.85rem;
  border-radius: 2px;
  border: 1px solid rgba(0, 0, 0, 0.35);
  /* A chevron-like notch evokes the in-game quality pip without bundling game art. */
  clip-path: polygon(50% 0, 100% 50%, 50% 100%, 0 50%);
}
</style>
