using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DeckTester.Components;

// Browser storage is available only after the interactive render, never during prerendering.
public abstract class PersistentSelectionPage<T> : ComponentBase where T : class
{
    [Inject] private IJSRuntime SelectionStorage { get; set; } = default!;
    protected abstract string SelectionKey { get; }
    protected abstract T CaptureSelections();
    protected abstract void RestoreSelections(T selections);
    protected bool SelectionsReady { get; private set; }
    private string? loadedKey, savedJson;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var key = "gauntlet:selections:v1:" + SelectionKey;
        try
        {
            if (loadedKey != key)
            {
                SelectionsReady = false;
                var stored = await SelectionStorage.InvokeAsync<string?>("localStorage.getItem", key);
                // Navigation may have changed the route while storage was being read.
                if (key != "gauntlet:selections:v1:" + SelectionKey) return;
                if (stored is not null)
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<T>(stored) is { } selections)
                            RestoreSelections(selections);
                    }
                    catch (JsonException) { } // Ignore malformed or obsolete browser data.
                }
                loadedKey = key;
                savedJson = stored;
                SelectionsReady = true;
                StateHasChanged();
                return;
            }

            var current = JsonSerializer.Serialize(CaptureSelections());
            if (current == savedJson) return;
            await SelectionStorage.InvokeVoidAsync("localStorage.setItem", key, current);
            savedJson = current;
        }
        catch (JSException) { EnableWithoutStorage(key); }
        catch (JSDisconnectedException) { }
    }

    private void EnableWithoutStorage(string key)
    {
        // Storage can be disabled by browser privacy settings; keep the page usable.
        if (SelectionsReady) return;
        loadedKey = key;
        SelectionsReady = true;
        StateHasChanged();
    }
}

public sealed record HomeSelections(List<string> DeckIds, int Games, int Workers, int Timeout,
    int TurnLimit, long Seed, bool StaggerStarts, double StartupSpacingSeconds, bool FollowLongestGame,
    bool ExplicitDeckSelection = false);
public sealed record ResultSelections(string Matchup, string Outcome, string Mana, int Page, string TimeUnit = "rounds");
public sealed record SessionLogSelections(string Deck,string Decision,string Status,string Matchup,string Outcome,string Mana,string TimeUnit,int Page);
public sealed record ReplaySelections(bool Audit);
