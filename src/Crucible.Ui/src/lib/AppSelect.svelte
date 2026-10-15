<script lang="ts">
  import { tick } from "svelte";
  let {
    id,
    label,
    options,
    value = $bindable(""),
    disabled = false,
    onchange = () => {},
  }: {
    id: string;
    label: string;
    options: { value: string; label: string }[];
    value?: string;
    disabled?: boolean;
    onchange?: (value: string) => void;
  } = $props();
  let open = $state(false),
    focused = $state(0);
  let trigger = $state<HTMLButtonElement>();
  let list = $state<HTMLDivElement>();
  let search = "",
    searchAt = 0;
  async function expand() {
    if (disabled) return;
    focused = Math.max(
      0,
      options.findIndex((o) => o.value === value),
    );
    open = true;
    await tick();
    list?.focus();
  }
  async function choose(index: number) {
    if (!options[index]) return;
    value = options[index].value;
    open = false;
    onchange(value);
    await tick();
    trigger?.focus();
  }
  function key(e: KeyboardEvent) {
    if (disabled) return;
    if (
      ["ArrowDown", "ArrowUp", "Home", "End", "Enter", " ", "Escape"].includes(
        e.key,
      )
    )
      e.preventDefault();
    if (e.key === "Escape") {
      open = false;
      trigger?.focus();
      return;
    }
    if (!open && ["ArrowDown", "ArrowUp", "Enter", " "].includes(e.key)) {
      void expand();
      return;
    }
    if (!open) return;
    if (e.key === "ArrowDown") focused = (focused + 1) % options.length;
    else if (e.key === "ArrowUp")
      focused = (focused - 1 + options.length) % options.length;
    else if (e.key === "Home") focused = 0;
    else if (e.key === "End") focused = options.length - 1;
    else if (e.key === "Enter" || e.key === " ") void choose(focused);
    else if (e.key.length === 1) {
      if (Date.now() - searchAt > 700) search = "";
      search += e.key.toLowerCase();
      searchAt = Date.now();
      const index = options.findIndex((o) =>
        o.label.toLowerCase().startsWith(search),
      );
      if (index >= 0) focused = index;
    }
  }
  function outside(e: PointerEvent) {
    if (open && !(e.target as Element).closest(`[data-select="${id}"]`))
      open = false;
  }
</script>

<svelte:window onpointerdown={outside} />
<div class="app-select" data-select={id}>
  <button
    bind:this={trigger}
    {id}
    type="button"
    class="select-trigger"
    role="combobox"
    aria-label={label}
    aria-controls={id + "-options"}
    aria-expanded={open}
    aria-haspopup="listbox"
    {disabled}
    onclick={() => (open ? (open = false) : expand())}
    onkeydown={key}
    >{options.find((o) => o.value === value)?.label ?? "Choose…"}<span
      aria-hidden="true">⌄</span
    ></button
  >
  {#if open}<div
      bind:this={list}
      class="select-options"
      id={id + "-options"}
      role="listbox"
      aria-label={label}
      tabindex="-1"
      aria-activedescendant={id + "-" + focused}
      onkeydown={key}
    >
      {#each options as option, index}<div
          id={id + "-" + index}
          role="option"
          tabindex="-1"
          aria-selected={value === option.value}
          class:focused={focused === index}
          onpointermove={() => (focused = index)}
          onclick={() => choose(index)}
          onkeydown={(e) => {
            if (e.key === "Enter") choose(index);
          }}
        >
          {option.label}
        </div>{/each}
    </div>{/if}
</div>
