namespace ChivRcon.Core;

/// <summary>Which Chivalry the server is running.</summary>
public enum GameFlavor
{
    /// <summary>Chivalry: Medieval Warfare -- BangMod / XangMod / AdminMod.</summary>
    Chivalry,

    /// <summary>Chivalry: Deadliest Warrior -- AdminModDW.</summary>
    DeadliestWarrior,
}

/// <summary>
/// The names behind the numbers on the wire.
///
/// The RCON protocol is identical between the two games -- AOCRCon.uc is byte-for-byte the
/// same file in both SDKs -- so nothing here changes what is sent. What changes is what the
/// numbers MEAN: team 1 is Mason in Medieval Warfare and Red in Deadliest Warrior, and class
/// index 2 is Vanguard in one and Viking in the other. Sending the right index with the
/// wrong label is the bug this exists to prevent.
///
/// <see cref="Current"/> is ambient rather than passed around because the app holds one
/// connection at a time (one Session, one RconClient, one PlayerTracker). If that ever stops
/// being true, take the flavour as a parameter -- every method here already has an overload
/// that does.
/// </summary>
public static class GameProfile
{
    private static GameFlavor _current = GameFlavor.Chivalry;

    /// <summary>
    /// The flavour in force. Set from the server bookmark on connect, then refined by
    /// <see cref="NoteClassName"/> if the server tells us otherwise.
    /// </summary>
    public static GameFlavor Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke();
        }
    }

    /// <summary>What the server's own data said, or null if nothing has said yet.</summary>
    public static GameFlavor? Detected { get; private set; }

    /// <summary>True once a source that cannot be wrong has spoken — see NoteServerInfo.</summary>
    public static bool DetectionIsAuthoritative { get; private set; }

    /// <summary>Mod the server reported, for the log. Empty if it did not say.</summary>
    public static string ServerMod { get; private set; } = "";

    /// <summary>
    /// The playable teams the server reported, by real team index. Empty against a server that
    /// does not send them, in which case the built-in tables are all we have.
    ///
    /// Worth trusting over any table: Deadliest Warrior runs one to six teams depending on the
    /// mode and the server's ?NumTeams= option, so "there are two teams" is a Medieval Warfare
    /// assumption. FFA and Duel have exactly one — which is why moving someone to "red" on an
    /// FFA map is refused rather than broken.
    /// </summary>
    public static IReadOnlyList<ServerTeam> ServerTeams { get; private set; } = Array.Empty<ServerTeam>();

    /// <summary>Raised when Current changes, so views can relabel.</summary>
    public static event Action? Changed;

    // Medieval Warfare: EAOCClass 0-4 (AOCPawn.uc:200). Peasant and King are 5-6 but are not
    // player-selectable, and AdminModRCon.HandleSetClass rejects anything above 4.
    private static readonly string[] ChivalryClasses =
        { "Archer", "Man-at-Arms", "Vanguard", "Knight" };

    // Deadliest Warrior: EAOCClass 0-5 (CDW AOCPawn.uc:197), in PlayerClasses order.
    private static readonly string[] DeadliestWarriorClasses =
        { "Samurai", "Spartan", "Viking", "Knight", "Ninja", "Pirate" };

    /// <summary>Class-picker labels, indexed by the value opcode 52 takes.</summary>
    public static IReadOnlyList<string> ClassNames(GameFlavor flavor) =>
        flavor == GameFlavor.DeadliestWarrior ? DeadliestWarriorClasses : ChivalryClasses;

    public static IReadOnlyList<string> ClassNames() => ClassNames(Current);

    /// <summary>The two teams a match is actually played between, for prompts and score rows.</summary>
    public static (string First, string Second) TeamPair(GameFlavor flavor) =>
        flavor == GameFlavor.DeadliestWarrior ? ("Blue", "Red") : ("Agatha", "Mason");

    public static (string First, string Second) TeamPair() => TeamPair(Current);

    /// <summary>
    /// Team name for a replicated TeamIndex.
    ///
    /// Prefers what the server actually reported — it knows its own map's teams, including a
    /// mode running more than the usual two. The tables below are the fallback for a server
    /// that does not send a team list.
    ///
    /// Deadliest Warrior's EAOCFaction is a different enum entirely -- six colour teams
    /// before the non-playing entries, where Medieval Warfare has two factions -- so the
    /// tables cannot be shared.
    /// </summary>
    public static string TeamName(int teamId, GameFlavor flavor)
    {
        foreach (var t in ServerTeams)
            if (t.Index == teamId && !string.IsNullOrWhiteSpace(t.Name))
                return t.Name;

        return TeamNameFromTable(teamId, flavor);
    }

    private static string TeamNameFromTable(int teamId, GameFlavor flavor) =>
        flavor == GameFlavor.DeadliestWarrior
            ? teamId switch
            {
                0 => "Blue",
                1 => "Red",
                2 => "Green",
                3 => "Pink",
                4 => "White",
                5 => "Black",
                6 => "Spectator",
                7 => "NPC",
                8 => "None",
                9 => "All",       // EFAC_ALL - all-chat / broadcasts
                10 => "FFA",
                _ => $"Team {teamId}",
            }
            : teamId switch
            {
                0 => "Agatha",
                1 => "Mason",
                2 => "FFA",
                3 => "Spectator",
                4 => "All",       // EFAC_ALL - all-chat / broadcasts
                _ => $"Team {teamId}",
            };

    /// <summary>
    /// Learn the flavour from a family class name in PLAYER_INFO (opcode 30).
    ///
    /// Deadliest Warrior's families are CDWFamilyInfo_Samurai and friends; Medieval Warfare's
    /// are AOCFamilyInfo_*. The string is empty until a player has picked a class, which is
    /// why this only refines the bookmark setting rather than replacing it -- an empty server
    /// never says anything.
    /// </summary>
    public static void NoteClassName(string? className)
    {
        if (string.IsNullOrEmpty(className)) return;

        GameFlavor seen;
        if (className.StartsWith("CDWFamilyInfo", StringComparison.OrdinalIgnoreCase))
            seen = GameFlavor.DeadliestWarrior;
        else if (className.StartsWith("AOCFamilyInfo", StringComparison.OrdinalIgnoreCase))
            seen = GameFlavor.Chivalry;
        else
            return;

        // SERVER_INFO already settled it, and it saw the server's own identity rather than
        // inferring from one player's class. Do not let a later guess overrule it.
        if (DetectionIsAuthoritative) return;

        if (Detected == seen) return;
        Detected = seen;
        Current = seen;
    }

    /// <summary>
    /// Learn the flavour from SERVER_INFO (opcode 37), which the client asks for on connect.
    ///
    /// This is the authoritative source and the only one that works on an empty server:
    /// AdminModDW appends its mod name and game to that reply, so the answer arrives before
    /// anybody has spawned. Three distinguishable cases:
    ///
    ///   game/mod names us as Deadliest Warrior -> DW.
    ///   names something else                   -> Medieval Warfare.
    ///   both empty                             -> an extended server predating these fields,
    ///                                             which can only be AdminMod / XangMod /
    ///                                             BangMod on Medieval Warfare.
    ///
    /// A vanilla server never answers opcode 36 at all, so this is not called and the
    /// bookmark setting stands until a class name arrives.
    /// </summary>
    public static void NoteServerInfo(string? modName, string? gameName)
    {
        ServerMod = modName ?? "";

        bool dw = string.Equals(gameName, "DeadliestWarrior", StringComparison.OrdinalIgnoreCase)
               || (modName ?? "").StartsWith("AdminModDW", StringComparison.OrdinalIgnoreCase);

        Detected = dw ? GameFlavor.DeadliestWarrior : GameFlavor.Chivalry;
        DetectionIsAuthoritative = true;
        Current = Detected.Value;
    }

    /// <summary>Record the live team list from SERVER_INFO. Empty list clears it.</summary>
    public static void NoteServerTeams(IReadOnlyList<ServerTeam>? teams)
    {
        var next = teams is { Count: > 0 } ? teams : Array.Empty<ServerTeam>();
        if (next.SequenceEqual(ServerTeams))
            return;

        ServerTeams = next;
        Changed?.Invoke();
    }

    /// <summary>Forget what a previous connection taught us. Called on connect and disconnect.</summary>
    public static void ResetDetection()
    {
        Detected = null;
        DetectionIsAuthoritative = false;
        ServerMod = "";
        ServerTeams = Array.Empty<ServerTeam>();
    }
}
