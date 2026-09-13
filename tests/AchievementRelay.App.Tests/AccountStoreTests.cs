using System.IO;
using AchievementRelay.App;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

internal static class AccountStoreTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AchievementRelay-AccountTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var services = new AppServices(new AppPaths(directory));
            var before = new AppSettings { DisplayName = "Before" };
            var edited = before with { DisplayName = "New local edit" };
            services.SettingsStore.SaveAsync(before).GetAwaiter().GetResult();
            services.SettingsStore.SaveAsync(edited).GetAwaiter().GetResult();
            if (services.SettingsStore.CompareAndSaveAccountAsync(before, before with { DisplayName = "Cloud" }).GetAwaiter().GetResult())
                throw new Exception("Sync overwrote a concurrent local settings edit.");
            if (services.SettingsStore.LoadAsync().GetAwaiter().GetResult().DisplayName != "New local edit")
                throw new Exception("Local settings were lost.");
            var achievement = new AchievementEvent { Id = "synthetic-account-history", Name = "Test", SourceProvider = "Steam", ImageBytes = [1, 2, 3] };
            services.CompanionJournal.MergeAccountHistoryAsync([new JournalEntry(achievement, DateTimeOffset.UtcNow, "test", "Delivered", DateTimeOffset.UtcNow)]).GetAwaiter().GetResult();
            var imported = services.CompanionJournal.Snapshot.Single().Achievement;
            if (!imported.IsHistorical || imported.ImageBytes is not null) throw new Exception("Imported account history is not presentation-only.");
            if (services.EventLedger.ContainsAsync(achievement.Id).GetAwaiter().GetResult()) throw new Exception("History import changed the delivery ledger.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
