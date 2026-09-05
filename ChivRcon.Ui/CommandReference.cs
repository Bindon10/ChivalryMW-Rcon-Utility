namespace ChivRcon.App;

/// <summary>
/// One row of the Reference page.
/// Public and top-level because Avalonia 12 compiled bindings need to name the type in
/// x:DataType, which rules out a private nested record.
/// </summary>
public sealed record CommandRef(string Name, string Where, string Needs, string Detail);

/// <summary>
/// The admin cheat-sheet. Sourced from the opcode table in
/// Development\RCON_PROTOCOL.md and from XangModRCon.uc -- keep them in step.
///
/// "Needs" is the honest support level against the servers this is actually pointed at:
/// stock Chivalry, or one running any of the mods that implement the extended opcode set
/// -- BangMod, XangMod or the standalone AdminMod. They share the same RCon source, so a
/// command is either in all three or in none. Several entries began life in ChivAdmin's
/// mutator, but these implement them, so that is what they require here -- the provenance
/// is a note in the description rather than a separate support tier.
/// Anything a server does not understand is ignored rather than erroring, so an extended
/// command simply does nothing on a vanilla box.
/// </summary>
public static class CommandReference
{
    public const string Vanilla = "Vanilla";

    /// <summary>
    /// Any server running the shared extended-RCON code. Kept as one tier on purpose:
    /// AdminMod is XangMod's RCon lifted out verbatim, so the opcode coverage is identical.
    /// </summary>
    public const string XangMod = "BangMod / XangMod / AdminMod";

    public static IReadOnlyList<CommandRef> All { get; } = new CommandRef[]
    {
        // ---- roster right-click ----
        new("Say to player", Gestures.RosterMenu, Vanilla,
            "Private message to one player. Opcode 8."),
        new("Kick", Gestures.RosterMenu, Vanilla,
            "Drops the player with a reason. They can rejoin immediately. Opcode 17."),
        new("Temp ban", Gestures.RosterMenu, Vanilla,
            "Ban with a duration in minutes. Expiry is checked lazily, when the banned player next tries to join. Opcode 18."),
        new("Ban", Gestures.RosterMenu, Vanilla,
            "Permanent ban. Records name, reason, SteamID and the player's IP as a DENY policy. Opcode 19."),
        new("Copy SteamID64 / Steam3", Gestures.RosterMenu, Vanilla,
            "Puts the id on the clipboard. Handy for the ban list or an external tool."),

        new("Kill", Gestures.RosterMenu, XangMod,
            "Kills the player where they stand. From ChivAdmin's mutator originally; XangMod implements it. Opcode 25."),
        new("Set score", Gestures.RosterMenu, XangMod,
            "Overwrites the player's score. From ChivAdmin's mutator originally. Opcode 24."),
        new("Inebriate", Gestures.RosterMenu, XangMod,
            "Drunk camera and slurred audio. ChivAdmin's readme hedged that it may be TO2-only; that was never the map, it was the post-process chain. XangMod adds the drunk chain when a map lacks one, so it works anywhere. Opcode 26."),
        new("Sober up", Gestures.RosterMenu, XangMod,
            "Undoes Inebriate. ChivAdmin had no equivalent — its version wore off on respawn. Opcode 48."),

        new("Text mute / unmute", Gestures.RosterMenu, XangMod,
            "Silences the player in chat. XangMod drops the message server-side; vanilla only sets a flag that each client is trusted to honour, which Steam friends of the muted player ignore. Opcode 42."),
        new("Force spectate", Gestures.RosterMenu, XangMod,
            "Moves the player to the spectator team. Opcode 33."),
        new("Move to Agatha / Move to Mason", Gestures.RosterMenu, XangMod,
            "Kills the current pawn so the swap lands immediately rather than on next respawn. Needs the player to have picked a class already. Opcode 32."),
        new("Console command on player", Gestures.RosterMenu, XangMod,
            "Runs a command against that player's server-side controller — not their machine. e.g. setname NEWNAME. Opcode 28, scope 1. Verbs in XangModBlockedConsoleCommands (default quit, exit, debug) are refused — scope 1 runs in the server's process, so quit on a player quits the server."),

        // ---- console page ----
        new("Broadcast: Say", "Console page", Vanilla,
            "Message to everyone, in the chat feed. Opcode 6."),
        new("Broadcast: Say (big)", "Console page", XangMod,
            "The same message, plus the objective header banner across the top of everyone's screen. Opcode 7: vanilla routes it to the chat handler, so the banner needs a server running XangMod."),
        new("Change map", "Console page", Vanilla,
            "Loads a map by name. The dropdown fills from the server's rotation on connect, but any name can be typed. Opcode 11."),
        new("Next map", "Console page", Vanilla,
            "Advances the rotation. Opcode 12."),
        new("Run on server", "Console page", XangMod,
            "Any console command, executed by the game. Output comes back into the feed — vanilla RCON discarded it. Opcode 28, scope 0."),

        // ---- console commands worth knowing ----
        new("addRedBots N / addBlueBots N", "Console command", XangMod,
            "Adds bots to one team. ChivAdmin's own suggested list."),
        new("AddBots N", "Console command", XangMod, "Adds bots to whichever team needs them."),
        new("KillBots", "Console command", XangMod, "Removes all bots."),
        new("ManuallyEndGame", "Console command", XangMod, "Ends the current match immediately."),
        new("AdminRestartMap", "Console command", XangMod, "Restarts the current map."),
        new("aoc_slomo N", "Console command", XangMod,
            "Game speed, 1 is normal. The client redirects this to opcode 46 rather than sending it as text, because AOCGame.SetGameSpeed has to notify clients and republish the speed — a bare console command does not."),
        new("AdminChangeTeamDamageAmount N", "Console command", XangMod,
            "Team damage multiplier, 0 to 1."),
        new("pause", "Console command", XangMod, "Pauses the match."),

        // ---- tournament page ----
        new("Tournament mode on/off", "Tournament page", XangMod,
            "Holds the pre-round until each team hits the ready threshold, and disables autobalance, the ping limit and the team-damage penalty — the same set the ?Tournament launch option applies. One-shot: AOCGame.StartRound clears it when a round begins, so set it again each round. Turning it off does not restore autobalance or the ping limit, because the server cannot know what they were. Opcode 49."),
        new("Force all ready", "Tournament page", XangMod,
            "Vanilla's AdminReadyAll, previously reachable only by typing !adminreadyall in chat. Opcode 50."),
        new("Clear all ready", "Tournament page", XangMod,
            "Clears every ready flag and the admin ready override, so the round gate counts players again. No vanilla equivalent; this is what a re-match needs. Opcode 50."),
        new("Pause / unpause", "Tournament page", XangMod,
            "Pauses at the GameInfo level, so it does not depend on an admin being connected. Both directions are announced in chat. In-game \"unpause\" cannot clear an RCON pause — unpause from here. Opcode 43."),
        new("Restart match", "Tournament page", XangMod, "Back to the pre-round. Opcode 47."),
        new("End match", "Tournament page", XangMod,
            "Ends the round with a chosen winner and a reason shown to players. Opcode 44."),
        new("Set team scores", "Tournament page", XangMod,
            "Overwrite either team's score, for fixing a bad round. On LTS this writes RoundScores and RoundsWon, not just the Teams[].Score mirror — the mirror is rewritten from RoundScores at every round boundary, so writing it alone was silently discarded. Note the match-end check compares for exact equality with the goal after adding the winning round, so a team parked on the goal steps over it; set one below. Opcode 34."),
        new("Game speed", "Tournament page", XangMod,
            "0.5x / normal / 2x. Uses opcode 46 rather than a console command, because AOCGame.SetGameSpeed has to notify clients and republish the speed."),

        // ---- loadout page ----
        new("Force class", "Loadout page", XangMod,
            "Moves a player to another class on their current team. Without \"Apply now\" it lands on their next spawn, which is vanilla behaviour; with it the pawn is killed so it takes effect immediately. Opcode 52."),
        new("Force loadout", "Loadout page", XangMod,
            "Sets primary / secondary / tertiary from the list the player's current class actually allows — the server sends the options (53) and the choice goes back as an index (56), so no weapon names cross the wire. Applies on next spawn."),
        new("Freeze / release", "Loadout page", XangMod,
            "Blocks movement, attacks, block, feint, sprint, dodge, jump and crouch, leaving Talk alone so they can reply. Uses Torn Banner's tutorial input system, which is enforced on the player's own client — TB's own comment says it is not a safe way to stop someone acting. Fine for holding an ordinary player still, useless against a modified client. Opcode 51."),

        // ---- teleport and slap ----
        new("Slap", Gestures.RosterMenu, XangMod,
            "Launches the player. No damage — Kill is there for that. Opcode 61."),
        new("Send to player", Gestures.RosterMenu, XangMod,
            "Teleports one player to another; pick the destination from a list. The server tries a ring of spots around the destination because SetLocation refuses an occupied one, so it can fail honestly — the audit line says why. Both must be alive. Opcode 60."),

        // ---- map page ----
        new("Player map", "Map page", XangMod,
            "Top-down plot of everyone, polled on a timer. Read live from Pawn.Location server-side rather than the 2s-stale replicated copy. No map data exists to scale against, so the view auto-fits to the players — the scale shifts as they spread out. Opcodes 57-59."),

        // ---- local, no server involved ----
        new("Player notes", Gestures.RosterMenu, "Local",
            "Free text, a warning counter and a watchlist flag per SteamID64, kept in %AppData%\\ChivRconUtility\\notes.json. Shows as a badge in the roster's Notes column and is announced in the feed when a noted player connects. Works while disconnected."),
        new("Add warning", Gestures.RosterMenu, "Local",
            "Bumps the warning counter and opens the note so the reason gets written down at the same time. \"This is his third\" is the point."),
        new("Name history", "automatic", "Local",
            "Every name seen per SteamID64, newest last. Renaming is the standard dodge, so the trail builds itself from PlayerConnect and NameChanged."),
        new("Audit log", "automatic", "Local",
            "Every command sent and every server audit reply, appended to %AppData%\\ChivRconUtility\\audit\\audit-YYYY-MM.jsonl with timestamp, admin and server. A 'sent' line with no matching 'audit' is what an ignored opcode looks like."),

        // ---- bans page ----
        new("Muted list", "Mutes page", XangMod,
            "Every stored mute, including players who are currently offline. Mutes persist in the server's ini like bans, so they survive a reconnect and a restart; unmuting an offline player clears the stored entry. Opcodes 62-64."),
        new("Ban list", "Bans page", XangMod,
            "Reads the server's own ban list. It lives in the server ini as globalconfig, so vanilla RCON had no way to send it — the page stays empty against a stock server. Opcodes 39-41."),
        new("Unban", "Bans page", Vanilla,
            "Lifts a ban by SteamID. Against a stock server the removal is not written back to the ini, so the ban returns on restart; XangMod saves it and reports whether anything matched. Opcode 20."),

        // ---- automatic ----
        new("Auto-reconnect", "Server page", Vanilla,
            "The RCON listener is a level actor, so it dies on every map change. This dials back in for up to 20 attempts, four seconds apart."),
        new("Player detail", "Dashboard", XangMod,
            "Score, kills, rank, idle time and team damage in the roster. Replaces the plain ping event. Opcode 23."),
    };
}
