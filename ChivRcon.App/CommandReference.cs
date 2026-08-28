namespace ChivRcon.App;

/// <summary>
/// One row of the Reference page.
/// Public and top-level because Avalonia 12 compiled bindings need to name the type in
/// x:DataType, which rules out a private nested record.
/// </summary>
public sealed record CommandRef(string Name, string Where, string Needs, string Detail);

/// <summary>
/// The admin cheat-sheet. Sourced from the opcode table in
/// Development\RCON_PROTOCOL.md and from BangModRCon.uc -- keep them in step.
///
/// "Needs" is the honest support level against the servers this is actually pointed at:
/// stock Chivalry, or one running BangMod. Several entries began life in ChivAdmin's
/// mutator, but BangModRCon implements them, so BangMod is what they require here -- the
/// provenance is a note in the description rather than a separate support tier.
/// Anything a server does not understand is ignored rather than erroring, so a command
/// listed as BangMod simply does nothing on a vanilla box.
/// </summary>
public static class CommandReference
{
    public const string Vanilla = "Vanilla";
    public const string BangMod = "BangMod";

    public static IReadOnlyList<CommandRef> All { get; } = new CommandRef[]
    {
        // ---- roster right-click ----
        new("Say to player", "Roster right-click", Vanilla,
            "Private message to one player. Opcode 8."),
        new("Kick", "Roster right-click", Vanilla,
            "Drops the player with a reason. They can rejoin immediately. Opcode 17."),
        new("Temp ban", "Roster right-click", Vanilla,
            "Ban with a duration in minutes. Expiry is checked lazily, when the banned player next tries to join. Opcode 18."),
        new("Ban", "Roster right-click", Vanilla,
            "Permanent ban. Records name, reason, SteamID and the player's IP as a DENY policy. Opcode 19."),
        new("Copy SteamID64 / Steam3", "Roster right-click", Vanilla,
            "Puts the id on the clipboard. Handy for the ban list or an external tool."),

        new("Kill", "Roster right-click", BangMod,
            "Kills the player where they stand. From ChivAdmin's mutator originally; BangMod implements it. Opcode 25."),
        new("Set score", "Roster right-click", BangMod,
            "Overwrites the player's score. From ChivAdmin's mutator originally. Opcode 24."),
        new("Inebriate", "Roster right-click", BangMod,
            "Drunk camera and slurred audio. ChivAdmin's readme hedged that it may be TO2-only; that was never the map, it was the post-process chain. BangMod adds the drunk chain when a map lacks one, so it works anywhere. Opcode 26."),
        new("Sober up", "Roster right-click", BangMod,
            "Undoes Inebriate. ChivAdmin had no equivalent — its version wore off on respawn. Opcode 48."),

        new("Text mute / unmute", "Roster right-click", BangMod,
            "Silences the player in chat. BangMod drops the message server-side; vanilla only sets a flag that each client is trusted to honour, which Steam friends of the muted player ignore. Opcode 42."),
        new("Force spectate", "Roster right-click", BangMod,
            "Moves the player to the spectator team. Opcode 33."),
        new("Move to Agatha / Move to Mason", "Roster right-click", BangMod,
            "Kills the current pawn so the swap lands immediately rather than on next respawn. Needs the player to have picked a class already. Opcode 32."),
        new("Console command on player", "Roster right-click", BangMod,
            "Runs a command against that player's server-side controller — not their machine. e.g. setname NEWNAME. Opcode 28, scope 1. Verbs in BangModBlockedConsoleCommands (default quit, exit, debug) are refused — scope 1 runs in the server's process, so quit on a player quits the server."),

        // ---- console page ----
        new("Broadcast: Say", "Console page", Vanilla,
            "Message to everyone, in the chat feed. Opcode 6."),
        new("Broadcast: Say (big)", "Console page", Vanilla,
            "Same, but as the large centre-screen banner. Opcode 7."),
        new("Change map", "Console page", Vanilla,
            "Loads a map by name. The dropdown fills from the server's rotation on connect, but any name can be typed. Opcode 11."),
        new("Next map", "Console page", Vanilla,
            "Advances the rotation. Opcode 12."),
        new("Run on server", "Console page", BangMod,
            "Any console command, executed by the game. Output comes back into the feed — vanilla RCON discarded it. Opcode 28, scope 0."),

        // ---- console commands worth knowing ----
        new("addRedBots N / addBlueBots N", "Console command", BangMod,
            "Adds bots to one team. ChivAdmin's own suggested list."),
        new("AddBots N", "Console command", BangMod, "Adds bots to whichever team needs them."),
        new("KillBots", "Console command", BangMod, "Removes all bots."),
        new("ManuallyEndGame", "Console command", BangMod, "Ends the current match immediately."),
        new("AdminRestartMap", "Console command", BangMod, "Restarts the current map."),
        new("aoc_slomo N", "Console command", BangMod,
            "Game speed, 1 is normal. The client redirects this to opcode 46 rather than sending it as text, because AOCGame.SetGameSpeed has to notify clients and republish the speed — a bare console command does not."),
        new("AdminChangeTeamDamageAmount N", "Console command", BangMod,
            "Team damage multiplier, 0 to 1."),
        new("pause", "Console command", BangMod, "Pauses the match."),

        // ---- tournament page ----
        new("Tournament mode on/off", "Tournament page", BangMod,
            "Holds the pre-round until each team hits the ready threshold, and disables autobalance, the ping limit and the team-damage penalty — the same set the ?Tournament launch option applies. One-shot: AOCGame.StartRound clears it when a round begins, so set it again each round. Turning it off does not restore autobalance or the ping limit, because the server cannot know what they were. Opcode 49."),
        new("Force all ready", "Tournament page", BangMod,
            "Vanilla's AdminReadyAll, previously reachable only by typing !adminreadyall in chat. Opcode 50."),
        new("Clear all ready", "Tournament page", BangMod,
            "Clears every ready flag and the admin ready override, so the round gate counts players again. No vanilla equivalent; this is what a re-match needs. Opcode 50."),
        new("Pause / unpause", "Tournament page", BangMod,
            "Pauses at the GameInfo level, so it does not depend on an admin being connected. Both directions are announced in chat. In-game \"unpause\" cannot clear an RCON pause (it only clears one the same player set) — use this, or !unpause. Opcode 43."),
        new("Restart match", "Tournament page", BangMod, "Back to the pre-round. Opcode 47."),
        new("End match", "Tournament page", BangMod,
            "Ends the round with a chosen winner and a reason shown to players. Opcode 44."),
        new("Set team scores", "Tournament page", BangMod,
            "Overwrite either team's score, for fixing a bad round. On LTS this writes RoundScores and RoundsWon, not just the Teams[].Score mirror — the mirror is rewritten from RoundScores at every round boundary, so writing it alone was silently discarded. Note the match-end check compares for exact equality with the goal after adding the winning round, so a team parked on the goal steps over it; set one below. Opcode 34."),
        new("Game speed", "Tournament page", BangMod,
            "0.5x / normal / 2x. Uses opcode 46 rather than a console command, because AOCGame.SetGameSpeed has to notify clients and republish the speed."),

        // ---- loadout page ----
        new("Force class", "Loadout page", BangMod,
            "Moves a player to another class on their current team. Without \"Apply now\" it lands on their next spawn, which is vanilla behaviour; with it the pawn is killed so it takes effect immediately. Opcode 52."),
        new("Force loadout", "Loadout page", BangMod,
            "Sets primary / secondary / tertiary from the list the player's current class actually allows — the server sends the options (53) and the choice goes back as an index (56), so no weapon names cross the wire. Applies on next spawn."),
        new("Freeze / release", "Loadout page", BangMod,
            "Blocks movement, attacks, block, feint, sprint, dodge, jump and crouch, leaving Talk alone so they can reply. Uses Torn Banner's tutorial input system, which is enforced on the player's own client — TB's own comment says it is not a safe way to stop someone acting. Fine for holding an ordinary player still, useless against a modified client. Opcode 51."),

        // ---- teleport and slap ----
        new("Slap", "Roster right-click", BangMod,
            "Launches the player. No damage — Kill is there for that. Also in game as !slap <name> [power]. Opcode 61."),
        new("Send to player", "Roster right-click", BangMod,
            "Teleports one player to another; pick the destination from a list. The server tries a ring of spots around the destination because SetLocation refuses an occupied one, so it can fail honestly — the audit line says why. Both must be alive. Opcode 60."),
        new("!bring <name>", "In-game chat", BangMod,
            "Pulls a player to you. Admin only; the message is consumed rather than broadcast. Partial names work, and an ambiguous one tells you how many matched instead of guessing."),
        new("!goto <name>", "In-game chat", BangMod, "Moves you to a player. Admin only."),
        new("!slap <name> [power]", "In-game chat", BangMod,
            "Launches a player. Power defaults to 400 and is clamped 50-2000. Admin only."),
        new("!pause / !unpause", "In-game chat", BangMod,
            "Pauses the match from chat. Admin only. Unlike vanilla's unpause, this clears a pause set by anyone — including one set over RCON."),

        // ---- map page ----
        new("Player map", "Map page", BangMod,
            "Top-down plot of everyone, polled on a timer. Read live from Pawn.Location server-side rather than the 2s-stale replicated copy. No map data exists to scale against, so the view auto-fits to the players — the scale shifts as they spread out. Opcodes 57-59."),

        // ---- local, no server involved ----
        new("Player notes", "Roster right-click", "Local",
            "Free text, a warning counter and a watchlist flag per SteamID64, kept in %AppData%\\ChivRconUtility\\notes.json. Shows as a badge in the roster's Notes column and is announced in the feed when a noted player connects. Works while disconnected."),
        new("Add warning", "Roster right-click", "Local",
            "Bumps the warning counter and opens the note so the reason gets written down at the same time. \"This is his third\" is the point."),
        new("Name history", "automatic", "Local",
            "Every name seen per SteamID64, newest last. Renaming is the standard dodge, so the trail builds itself from PlayerConnect and NameChanged."),
        new("Audit log", "automatic", "Local",
            "Every command sent and every server audit reply, appended to %AppData%\\ChivRconUtility\\audit\\audit-YYYY-MM.jsonl with timestamp, admin and server. A 'sent' line with no matching 'audit' is what an ignored opcode looks like."),

        // ---- bans page ----
        new("Ban list", "Bans page", BangMod,
            "Reads the server's own ban list. It lives in the server ini as globalconfig, so vanilla RCON had no way to send it — the page stays empty against a stock server. Opcodes 39-41."),
        new("Unban", "Bans page", Vanilla,
            "Lifts a ban by SteamID. Against a stock server the removal is not written back to the ini, so the ban returns on restart; BangMod saves it and reports whether anything matched. Opcode 20."),

        // ---- automatic ----
        new("Auto-reconnect", "Server page", Vanilla,
            "The RCON listener is a level actor, so it dies on every map change. This dials back in for up to 20 attempts, four seconds apart."),
        new("Player detail", "Dashboard", BangMod,
            "Score, kills, rank, idle time and team damage in the roster. Replaces the plain ping event. Opcode 23."),
    };
}
