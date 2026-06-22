<template>
  <fieldset class="border p-3 h-100">
    <legend>Heat pipes</legend>
    <div class="form-check">
      <input type="checkbox" class="form-check-input" id="add-heat-pipes" v-model="addHeatPipes" />
      <label class="form-check-label" for="add-heat-pipes"> Add heatpipes for Aquilo ❄️ </label>
    </div>
  </fieldset>
</template>

<script lang="ts">
import { storeToRefs } from "pinia"
import { pick } from "../lib/helpers"
import { useOilFieldStore } from "../stores/OilFieldStore"

export default {
  props: {
    showAdvancedOptions: {
      type: Boolean,
      required: true,
    },
  },
  data() {
    return pick(storeToRefs(useOilFieldStore()), "addHeatPipes")
  },
}
</script>
