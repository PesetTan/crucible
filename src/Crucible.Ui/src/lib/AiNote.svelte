<script lang="ts">
  import { api } from "./api.ts";
  import { date } from "./data.ts";
  import type { Note } from "./types.ts";
  let {
    scope = "page",
    subject,
    context,
  }: { scope?: string; subject: string; context?: string } = $props();
  let notes = $state<Note[]>([]);
  $effect(() => {
    const controller = new AbortController();
    void api<Note[]>(
      `/api/v1/ai-notes?scope=${encodeURIComponent(scope)}&subject=${encodeURIComponent(subject)}`,
      { signal: controller.signal },
    )
      .then((value) => (notes = value))
      .catch(() => {});
    return () => controller.abort();
  });
</script>

{#if notes[0]}<details class="ai-note">
    <summary
      ><span class="ai-note-label">AI NOTE</span><span class="ai-note-title"
        >{notes[0].title}</span
      ></summary
    >
    <div class="ai-note-content">
      {#if context && notes[0].context && context !== notes[0].context}<p
          class="small muted"
        >
          Written for an earlier context. Review its saved evidence.
        </p>{/if}
      <p class="rules-text">{notes[0].body}</p>
      <footer class="ai-note-signature">
        — {notes[0].author} · {notes[0].model} · {date(notes[0].writtenAt)}
      </footer>
      <details class="ai-note-evidence">
        <summary>Evidence &amp; authorship</summary>
        <p>{notes[0].evidence}</p>
        <p>
          Model: {notes[0].exactModelId ??
            "Exact runtime identity was not exposed."}
        </p>
      </details>
      {#if notes.length > 1}<details class="ai-note-history">
          <summary>Earlier notes · {notes.length - 1}</summary
          >{#each notes.slice(1) as note}<article>
              <h4>{note.title}</h4>
              <p>{note.body}</p>
              <p class="small muted">
                {note.author} · {note.model} · {date(note.writtenAt)}
              </p>
            </article>{/each}
        </details>{/if}
    </div>
  </details>{/if}
