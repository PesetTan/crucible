<script lang="ts">
  import type { Snippet } from "svelte";
  let {
    title,
    eyebrow,
    description,
    heading,
    actions,
  }: {
    title: string;
    eyebrow?: string;
    description?: string;
    heading?: Snippet;
    actions?: Snippet;
  } = $props();
</script>

<header class="page-header">
  {#if eyebrow}<div class="eyebrow">{eyebrow}</div>{/if}
  <div class="page-header-row">
    <div class="page-header-copy">
      {#if heading}{@render heading()}{:else}<h1>{title}</h1>{/if}
      {#if description}<p class="lead">{description}</p>{/if}
    </div>
    {#if actions}<div class="page-header-actions">{@render actions()}</div>{/if}
  </div>
</header>
