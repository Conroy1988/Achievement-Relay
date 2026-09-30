namespace AchievementRelay.App.Services;

/// <summary>Explanations only. Journal text never authorizes a send or retry.</summary>
public static class DeliveryStatusPresentation
{
    public static string Explain(JournalEntry entry)
    {
        if (entry.Achievement.IsHistorical)
            return "Imported or synced history is for your collection only. It will not create a new Discord post.";
        return entry.Delivery switch
        {
            "Delivered" => "Discord accepted this post. No retry is needed.",
            "Delivered on another PC" => "A shared delivery record says another PC completed this post. This PC did not send it again.",
            "Delivered (confirmed by you)" => "You confirmed the existing post in Discord. Relay recorded that confirmation without sending again.",
            "Filtered" => "Your delivery rules excluded this unlock. It remains in your collection; this is not a connection failure.",
            "Needs connection" => "Open Connections and check your Discord webhook. This status does not confirm a Discord post.",
            "Shared delivery unavailable" => "Relay could not safely acquire the shared delivery record. Check the shared folder before trying again.",
            "Pending" => "The unlock is recorded, but delivery is not yet confirmed. Refresh its status before taking action.",
            "Retry pending" => "Delivery has not completed. Check Connections and the activity log; retry still uses the existing duplicate-prevention checks.",
            var status when status.StartsWith("Delivery uncertain", StringComparison.Ordinal) =>
                "Discord may already have received this post. Check the channel first. Only confirm an existing post if you can see it; an uncertain outcome is not permission to resend.",
            var status when status.StartsWith("Claimed on another device", StringComparison.Ordinal) =>
                "Another device holds the delivery claim. This is not proof that Discord received the post. Check Discord and that device; do not force a second send.",
            _ => "Delivery is not confirmed by this status. Check the activity log and Discord before taking action."
        };
    }
}
