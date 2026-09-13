using System.IO;
using AchievementRelay.Core.Models;
using AchievementRelay.App.Services;

namespace AchievementRelay.App;

public sealed partial class CompanionWindow
{
    private void DiscardGameEdits()
    {
        _savedGameControls = null;
        SelectAchievement();
        RefreshGallery();
        _notice.Text = "Saved game preferences restored. Current filters applied.";
    }

    // Invoked only by the isolated native preview runner, never by normal startup.
    internal async Task VerifyUsabilityAsync()
    {
        void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        RefreshGallery(); RefreshSessions(); RefreshTrophies();
        Require(_gallery.Items.Count == 300, "The bounded 300-entry gallery was not populated.");
        Require(SessionEntries.Length > 0 && SessionEntries.All(x => !x.Achievement.IsHistorical), "Imported history entered the local session recap.");
        Require(_trophies.Items.OfType<TrophyRow>().Where(x => x.Achievement.IsHistorical).All(x => x.Imported), "A synced trophy was labelled as live.");
        _gallery.SelectedItem = _gallery.Items.OfType<Row>().First(x => x.Entry.Achievement.IsHistorical);
        Require(_retryButton?.IsEnabled == false && _confirmButton?.IsEnabled == false, "Historical recovery actions were enabled.");
        await RetryAsync();
        await ConfirmUncertainAsync();

        _gallery.SelectedItem = _gallery.Items.OfType<Row>().First(x => !x.Entry.Achievement.IsHistorical);
        var selectedId = Selected!.Entry.Achievement.Id;
        _muteGame.IsChecked = !_muteGame.IsChecked;
        _search.Text = "no-such-game-in-this-fixture";
        Require(GameDraft && Selected?.Entry.Achievement.Id == selectedId, "Filtering lost a per-game draft.");
        DiscardGameEdits();
        Require(!GameDraft && _gallery.Items.Count == 0, "Discard did not apply a pending gallery filter.");
        _search.Text = "";
        Require(_gallery.Items.Count == 300 && !GameDraft, "Clearing the filter left stale per-game edits.");

        _librarySearch.Text = "Fixture game 099";
        Require(_libraryGames.Items.Count == 1, "Library search did not find the requested game.");
        Require(_libraryDetails.Text.Contains("Fixture game 099"), "Library details do not follow selection.");
        _librarySearch.Text = "no-such-library-game";
        Require(_libraryGames.Items.Count == 0 && _libraryArt.Source is null && _libraryHistory.Items.Count == 0, "Empty library search retained stale artwork or history.");
        _librarySearch.Text = "";
        _librarySort.SelectedIndex = 1;
        var names = _libraryGames.Items.OfType<GameRow>().Select(x => x.Game.Name).ToArray();
        Require(names.Length == 100 && names.SequenceEqual(names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)), "Library name sorting failed.");

        var baseline = _savedCompanionControls;
        _scale.Value = 1.25;
        var blocker = _services.Paths.SettingsFile + ".tmp";
        Require(!File.Exists(blocker) && !Directory.Exists(blocker), "Save-failure fixture path already exists.");
        Directory.CreateDirectory(blocker);
        try
        {
            var failed = false;
            try { await SaveControlsAsync(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            Require(failed && _savedCompanionControls == baseline && CompanionControls != baseline && _scale.Value == 1.25,
                "A failed Companion save lost or accepted the draft.");
        }
        finally { Directory.Delete(blocker); }
        await SaveControlsAsync();
        Require(_savedCompanionControls == CompanionControls && (await _services.SettingsStore.LoadAsync()).Companion.OverlayScale == 1.25,
            "Companion save retry did not persist the draft.");
        _notice.Text = "Usability checks passed with 300 achievements and 100 games.";
    }
}
