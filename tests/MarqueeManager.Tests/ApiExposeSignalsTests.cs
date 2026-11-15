using System.Text.Json;
using RetroBatMarqueeManager.Application.Services;
using Xunit;

namespace MarqueeManager.Tests;

public class ApiExposeSignalsTests
{
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    // ── the mode of the world board ──────────────────────────────────────────

    [Fact]
    public void World_board_names_the_mode_being_played()
    {
        var info = WorldBoard.Read(Json("""
            { "present": true, "rom_group": "bublbobl", "ruleset": "1cc-power-up", "system": "mame",
              "ruleset_source": "played", "ruleset_label": "1CC-PU", "played_ruleset": "1cc-power-up",
              "difficulty": [ { "address": "0x1005", "name": "DIFFICULTY", "value": 3, "initial": 3, "measured": true } ] }
            """));

        Assert.Equal("1CC-PU", info.Label);
        Assert.Equal("played", info.Source);
        Assert.Equal("mame", info.System);
        Assert.Equal("DIFFICULTY 3", info.Difficulty);
    }

    [Fact]
    public void A_difficulty_that_was_not_measured_is_not_shown()
    {
        var info = WorldBoard.Read(Json("""
            { "ruleset": "1cc", "ruleset_label": "1CC",
              "difficulty": [ { "name": "DIFFICULTY", "value": 3, "initial": 3, "measured": false } ] }
            """));

        Assert.Equal(string.Empty, info.Difficulty);
    }

    [Fact]
    public void The_label_is_found_in_the_list_of_modes_when_the_shortcut_is_missing()
    {
        var info = WorldBoard.Read(Json("""
            { "ruleset": "1lc", "modes": [
                { "ruleset": "1cc", "label": "1CC", "selected": false },
                { "ruleset": "1lc", "label": "1LC", "selected": true } ] }
            """));

        Assert.Equal("1LC", info.Label);
    }

    [Fact]
    public void An_older_api_gives_no_label_and_nothing_breaks()
    {
        var info = WorldBoard.Read(Json("""{ "present": true, "rom_group": "sf2", "ruleset": "1cc", "leaderboard": [] }"""));

        Assert.Equal(string.Empty, info.Label);
        Assert.True(WorldBoard.IsForSelection(info, "fbneo"));
    }

    [Theory]
    [InlineData("mame", "mame", true)]
    [InlineData("mame", "MAME", true)]
    [InlineData("mame", "megadrive", false)]
    [InlineData("mame", "", true)]
    [InlineData("", "megadrive", true)]
    public void A_board_for_another_system_is_not_for_this_selection(string answered, string selected, bool expected)
        => Assert.Equal(expected, WorldBoard.IsForSelection(WorldBoardInfo.Unknown with { System = answered }, selected));

    [Theory]
    [InlineData("{name} - WORLD RANKING {mode}", "1LC", "{name} - WORLD RANKING 1LC")]
    [InlineData("{name} - WORLD RANKING {mode}", "", "{name} - WORLD RANKING")]
    [InlineData("{mode} · {name}", "1CC-PU", "1CC-PU · {name}")]
    [InlineData("{name} - {mode}", "", "{name}")]
    [InlineData("MY OWN TITLE", "1LC", "MY OWN TITLE")]
    [InlineData("", "1LC", "")]
    public void The_mode_goes_where_the_title_asks_for_it(string title, string label, string expected)
        => Assert.Equal(expected, WorldBoard.TitleWithMode(title, label));

    // ── one start per launch ─────────────────────────────────────────────────

    [Fact]
    public void A_start_announced_twice_is_one_start()
    {
        var launches = new LaunchTracker();

        Assert.False(launches.IsRepeatedStart("0123456789abcdef0123456789abcdef"));
        Assert.True(launches.IsRepeatedStart("0123456789ABCDEF0123456789ABCDEF"));
    }

    [Fact]
    public void After_the_end_the_same_game_starts_again()
    {
        var launches = new LaunchTracker();
        launches.IsRepeatedStart("aaa");
        launches.Ended("aaa");

        Assert.False(launches.IsRepeatedStart("aaa"));
    }

    [Fact]
    public void An_older_api_without_identifier_never_hides_a_start()
    {
        var launches = new LaunchTracker();

        Assert.False(launches.IsRepeatedStart(null));
        Assert.False(launches.IsRepeatedStart(""));
        Assert.False(launches.IsRepeatedStart(null));
    }

    [Fact]
    public void A_late_end_of_a_previous_launch_does_not_forget_the_current_one()
    {
        var launches = new LaunchTracker();
        launches.IsRepeatedStart("first");
        launches.IsRepeatedStart("second");
        launches.Ended("first");

        Assert.True(launches.IsRepeatedStart("second"));
    }

    // ── the meaning of the signals ───────────────────────────────────────────

    private const string Semantics19xx = """
        { "Schema": "nelfeplay.deck.semantics/1", "LaunchId": "launch-1", "SystemId": "fbneo", "Rom": "19xx", "Status": "loaded",
          "Signals": [
            { "Id": "players.1.resources.bombs", "Player": 1, "Kind": "counter", "Unit": "bomb", "Label": "Bombs", "Short": "BOMB",
              "From": [ { "Address": "0xFF8305", "Action": "BOMB_FIRED", "Player": null },
                        { "Address": "0xFF8305", "Action": "AMMO_GAIN", "Player": 1 } ],
              "Values": [], "Scale": { "Num": 1, "Den": 1, "Add": 0 }, "Min": 0, "Max": null, "Initial": 3,
              "Quality": "qualified", "Available": true },
            { "Id": "players.2.resources.bombs", "Player": 2, "Kind": "counter", "Unit": "bomb", "Label": "Bombs", "Short": "BOMB",
              "From": [ { "Address": "0xFF8405", "Action": "BOMB_FIRED", "Player": 2 } ],
              "Values": [], "Scale": { "Num": 1, "Den": 1, "Add": 0 }, "Min": 0, "Initial": 3, "Available": true },
            { "Id": "players.1.power.level", "Player": 1, "Kind": "level", "Label": "Shot", "Short": "SHOT",
              "From": [ { "Address": "0xFF8337", "Action": "WEAPON_STATE", "Player": 1 } ],
              "Values": [ { "Raw": 0, "Value": 1, "Label": "Normal" }, { "Raw": 255, "Value": 2, "Label": "Boosted" } ],
              "Scale": { "Num": 1, "Den": 1, "Add": 0 }, "Min": 1, "Max": 2, "Initial": 1, "Available": true },
            { "Id": "players.1.shield", "Player": 1, "Kind": "gauge", "Label": "Shield",
              "From": [ { "Address": 4096, "Action": "SHIELD", "Player": 1 } ],
              "Values": [ { "RawMin": 200, "RawMax": 255, "Value": 0, "Label": "Broken" } ],
              "Scale": { "Num": 100, "Den": 64, "Add": 0 }, "Available": true },
            { "Id": "players.1.absent", "Kind": "counter", "From": [ { "Address": "0x10", "Action": "X" } ], "Available": false }
          ],
          "Instruments": [
            { "Id": "p1.bombs", "Label": "Bombs", "Player": 1, "Show": "pips", "Signals": [ "players.1.resources.bombs" ] },
            { "Id": "p1.ghost", "Label": "Ghost", "Player": 1, "Show": "bar", "Signals": [ "players.1.absent" ] }
          ] }
        """;

    [Fact]
    public void Semantics_give_the_starting_value_before_any_change()
    {
        var semantics = new SignalSemantics();

        Assert.True(semantics.Load(Json(Semantics19xx)));

        var bombs = Assert.Single(semantics.Readings, r => r.Signal.Id == "players.1.resources.bombs");
        Assert.Equal(3, bombs.Value);
        Assert.Equal("launch-1", semantics.LaunchId);
        // a signal the game does not have is neither listed nor drawn
        Assert.DoesNotContain(semantics.Signals, s => s.Id == "players.1.absent");
        Assert.Single(semantics.Instruments);
    }

    [Fact]
    public void A_memory_change_moves_the_signal_it_belongs_to()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        var changed = semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 2);

        var reading = Assert.Single(changed);
        Assert.Equal("players.1.resources.bombs", reading.Signal.Id);
        Assert.Equal(2, reading.Value);
        Assert.Equal("BOMB", reading.Signal.Short);
    }

    [Fact]
    public void The_same_value_twice_is_not_a_change()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));
        semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 2);

        Assert.Empty(semantics.Apply("0xff8305", "bomb_fired", 1, 2));
    }

    [Fact]
    public void Address_action_and_player_must_all_match()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        Assert.Empty(semantics.Apply("0xFF8305", "SOMETHING_ELSE", 1, 0));
        Assert.Empty(semantics.Apply("0xFF8306", "BOMB_FIRED", 1, 0));
        // player 2's line, announced for player 1: not that signal
        Assert.Empty(semantics.Apply("0xFF8405", "BOMB_FIRED", 1, 0));
        Assert.Single(semantics.Apply("0xFF8405", "BOMB_FIRED", 2, 0));
        // an event that names no player still reaches a line that names one
        Assert.Single(semantics.Apply("0xFF8337", "WEAPON_STATE", null, 255));
    }

    [Fact]
    public void A_raw_value_takes_the_games_own_word_when_there_is_one()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        var boosted = Assert.Single(semantics.Apply("0xFF8337", "WEAPON_STATE", 1, 255));
        Assert.Equal(2, boosted.Value);
        Assert.Equal("Boosted", boosted.Text);
    }

    [Fact]
    public void A_scale_and_a_range_of_raw_values_are_applied()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        Assert.Equal(50, Assert.Single(semantics.Apply("0x1000", "SHIELD", 1, 32)).Value);
        var broken = Assert.Single(semantics.Apply("4096", "SHIELD", 1, 230));
        Assert.Equal(0, broken.Value);
        Assert.Equal("Broken", broken.Text);
    }

    [Fact]
    public void Readings_survive_semantics_sent_again_for_the_same_launch()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));
        semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 1);

        Assert.False(semantics.Load(Json(Semantics19xx)));

        Assert.Equal(1, Assert.Single(semantics.Readings, r => r.Signal.Id == "players.1.resources.bombs").Value);
    }

    [Fact]
    public void Another_launch_starts_from_the_starting_values()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));
        semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 0);

        Assert.True(semantics.Load(Json(Semantics19xx.Replace("launch-1", "launch-2"))));

        Assert.Equal(3, Assert.Single(semantics.Readings, r => r.Signal.Id == "players.1.resources.bombs").Value);
    }

    [Fact]
    public void A_game_without_semantics_has_no_signal()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        semantics.Load(Json("""{ "LaunchId": "launch-3", "Rom": "sf2", "Status": "absent", "Signals": [], "Instruments": [] }"""));

        Assert.False(semantics.HasSignals);
        Assert.Empty(semantics.Readings);
        Assert.Empty(semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 2));
    }

    [Fact]
    public void The_end_of_the_game_forgets_everything()
    {
        var semantics = new SignalSemantics();
        semantics.Load(Json(Semantics19xx));

        semantics.Clear();

        Assert.False(semantics.HasSignals);
        Assert.Equal(string.Empty, semantics.LaunchId);
    }

    [Theory]
    [InlineData("0xFF8305", 0xFF8305L)]
    [InlineData("0XFF8305", 0xFF8305L)]
    [InlineData("FF8305h", 0xFF8305L)]
    [InlineData("4096", 4096L)]
    [InlineData("", null)]
    [InlineData("score", null)]
    public void Addresses_are_read_in_the_spellings_the_streams_use(string text, long? expected)
        => Assert.Equal(expected, SignalSemantics.ParseAddress(text));

    // ── the named streams ────────────────────────────────────────────────────

    [Theory]
    [InlineData("""["frontend","scoring","multiplayer"]""")]
    [InlineData("""{ "Streams": [ { "Name": "frontend" }, { "Name": "scoring" }, { "Name": "multiplayer" } ] }""")]
    [InlineData("""{ "streams": [ { "name": "/ws/scoring", "RetainedEventTypes": [] }, { "name": "ws/multiplayer" }, { "name": "frontend" } ] }""")]
    public void The_streams_an_api_serves_are_read_whatever_the_listing_looks_like(string json)
    {
        var names = NamedStreams.Announced(json);

        Assert.Contains("scoring", names);
        Assert.Contains("multiplayer", names);
        Assert.Contains("frontend", names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "Streams": [ { "Name": "frontend" } ] }""")]
    public void An_api_that_does_not_announce_them_opens_nothing_more(string json)
        => Assert.DoesNotContain("scoring", NamedStreams.Announced(json));

    // ── what an instrument draws ─────────────────────────────────────────────

    private static SemanticSignal Signal(string id, int player = 1, string kind = "counter", string unit = "",
        long? min = 0, long? max = null, long? initial = null, string shortName = "")
        => new(id, player, kind, unit, "", shortName, min, max, initial);

    [Fact]
    public void Auto_shows_what_can_run_out_ammunition_first()
    {
        var signals = new[]
        {
            Signal("players.1.weapon.id", kind: "enum"),
            Signal("players.1.resources.bombs"),
            Signal("players.1.resources.ammo"),
            Signal("players.2.resources.ammo", player: 2)
        };

        Assert.Equal("players.1.resources.ammo", InstrumentPlan.Pick(signals, 1, "auto")!.Id);
        Assert.Equal("players.2.resources.ammo", InstrumentPlan.Pick(signals, 2, "")!.Id);
        Assert.Null(InstrumentPlan.Pick(signals, 3, "auto"));
    }

    [Fact]
    public void A_chosen_signal_is_found_by_its_full_name_its_end_or_its_unit()
    {
        var signals = new[]
        {
            Signal("players.1.resources.ammo"),
            Signal("players.1.resources.bombs", unit: "bomb"),
            Signal("players.2.resources.bombs", player: 2)
        };

        Assert.Equal("players.1.resources.bombs", InstrumentPlan.Pick(signals, 1, "resources.bombs")!.Id);
        Assert.Equal("players.2.resources.bombs", InstrumentPlan.Pick(signals, 2, "bombs")!.Id);
        Assert.Equal("players.1.resources.bombs", InstrumentPlan.Pick(signals, 1, "bomb")!.Id);
        Assert.Equal("players.2.resources.bombs", InstrumentPlan.Pick(signals, 1, "players.2.resources.bombs")!.Id);
        Assert.Null(InstrumentPlan.Pick(signals, 1, "shield"));
    }

    [Fact]
    public void Auto_never_draws_a_timer_or_a_used_count_as_a_reserve()
    {
        var signals = new[]
        {
            Signal("players.1.section.0.time", unit: "frame"),
            Signal("players.1.stun.timeout", unit: "frame"),
            Signal("players.1.bombs.used", unit: "bomb"),
            Signal("players.1.weapon.id", kind: "enum")
        };

        Assert.Null(InstrumentPlan.Pick(signals, 1, "auto"));
        // …but what the operator names is shown, whatever it is
        Assert.Equal("players.1.weapon.id", InstrumentPlan.Pick(signals, 1, "weapon.id")!.Id);
    }

    [Fact]
    public void A_role_finds_both_spellings_the_games_use()
    {
        Assert.Equal("players.1.bombs", InstrumentPlan.Pick(new[] { Signal("players.1.bombs.used"), Signal("players.1.bombs") }, 1, "bombs")!.Id);
        Assert.Equal("players.1.resources.bombs", InstrumentPlan.Pick(new[] { Signal("players.1.resources.bombs") }, 1, "bombs")!.Id);
        Assert.Equal("players.1.lives", InstrumentPlan.Pick(new[] { Signal("players.1.bombs"), Signal("players.1.lives") }, 1, "lives")!.Id);
        Assert.Equal("players.1.bombs", InstrumentPlan.Pick(new[] { Signal("players.1.lives"), Signal("players.1.bombs") }, 1, "auto")!.Id);
    }

    [Theory]
    [InlineData(null, 3L, 3L, 3L, 3)]      // no declared maximum: what the game starts with
    [InlineData(null, 3L, 4L, 4L, 4)]      // one more place the day a fourth bomb is seen
    [InlineData(6L, 3L, 2L, 3L, 6)]        // a declared maximum is the size, whatever was seen
    [InlineData(200L, 30L, 30L, 30L, 0)]   // too many to count: a continuous bar
    [InlineData(0L, 0L, 0L, 0L, 1)]        // never an instrument with no place at all
    public void The_number_of_places_follows_the_game(long? max, long initial, long value, long highest, int expected)
        => Assert.Equal(expected,
            InstrumentPlan.Places(Signal("players.1.resources.bombs", max: max, initial: initial), value, highest));

    [Fact]
    public void The_fill_is_the_share_between_floor_and_top()
    {
        var ammo = Signal("players.1.resources.ammo", max: 200);

        Assert.Equal(0.25, InstrumentPlan.Fill(ammo, 50, 200));
        Assert.Equal(0, InstrumentPlan.Fill(ammo, 0, 200));
        Assert.Equal(1, InstrumentPlan.Fill(ammo, 999, 200));
    }

    [Fact]
    public void Running_dry_is_a_spendable_quantity_on_its_floor()
    {
        Assert.True(InstrumentPlan.IsEmpty(Signal("players.1.resources.ammo"), 0));
        Assert.False(InstrumentPlan.IsEmpty(Signal("players.1.resources.ammo"), 1));
        Assert.True(InstrumentPlan.IsEmpty(Signal("players.1.lives", min: 1), 1));
        // a weapon whose identifier is 0 is not an empty weapon
        Assert.False(InstrumentPlan.IsEmpty(Signal("players.1.weapon.id", kind: "enum"), 0));
    }

    [Theory]
    [InlineData("players.1.resources.ammo", "", null, false, "RELOAD")]
    [InlineData("players.1.resources.ammo", "", null, true, "RECHARGEZ")]
    [InlineData("players.1.gun", "bullet", null, false, "RELOAD")]
    [InlineData("players.1.resources.bombs", "bomb", null, false, "EMPTY")]
    [InlineData("players.1.resources.bombs", "bomb", null, true, "VIDE")]
    [InlineData("players.1.resources.ammo", "", "  SHOOT OUTSIDE  ", false, "SHOOT OUTSIDE")]
    public void The_empty_message_fits_what_ran_out(string id, string unit, string? custom, bool french, string expected)
        => Assert.Equal(expected, InstrumentPlan.EmptyText(Signal(id, unit: unit), custom, french));

    [Fact]
    public void The_caption_is_the_short_name_then_the_unit()
    {
        Assert.Equal("BOMB", InstrumentPlan.Caption(Signal("x", shortName: "Bomb", unit: "bomb")));
        Assert.Equal("BULLET", InstrumentPlan.Caption(Signal("x", unit: "bullet")));
    }

    // ── the verdict of a certified score ─────────────────────────────────────

    [Fact]
    public void A_published_score_says_its_rank_and_its_mode()
    {
        var banner = VerdictBanner.For("published", 1520300, 3, "1CC-PU", french: false);

        Assert.Equal("CERTIFIED SCORE", banner!.Value.Title);
        Assert.Equal("1 520 300 · 1CC-PU · WORLD RANK 3", banner.Value.Detail);
    }

    [Fact]
    public void A_refused_score_never_shows_a_rank()
    {
        var banner = VerdictBanner.For("rejected", 900, 12, "", french: true);

        Assert.Equal("SCORE NON CERTIFIÉ", banner!.Value.Title);
        Assert.Equal("900", banner.Value.Detail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("something-new")]
    [InlineData(null)]
    public void An_unknown_status_says_nothing(string? status)
        => Assert.Null(VerdictBanner.For(status, 100, 1, "1CC", french: false));

    // ── against a real reply of APIExpose 1.9.185 ────────────────────────────

    [Fact]
    public void A_real_reply_for_19xx_gives_its_bombs_and_follows_them()
    {
        var reply = Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "semantics-19xx.json")));
        var semantics = new SignalSemantics();

        Assert.True(semantics.Load(reply));

        Assert.Equal(10, semantics.Signals.Count);
        var bombs = InstrumentPlan.Pick(semantics.Signals, 1, "auto");
        Assert.Equal("players.1.bombs", bombs!.Id);
        Assert.Equal("BOMB", InstrumentPlan.Caption(bombs));
        Assert.Equal(3, Assert.Single(semantics.Readings, r => r.Signal.Id == "players.1.bombs").Value);
        Assert.Equal(3, InstrumentPlan.Places(bombs, 3, 3));

        // a bomb is dropped, then the last ones
        Assert.Equal(2, Assert.Single(semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 2)).Value);
        var none = Assert.Single(semantics.Apply("0xFF8305", "BOMB_FIRED", 1, 0));
        Assert.True(InstrumentPlan.IsEmpty(none.Signal, none.Value));
        Assert.Equal("EMPTY", InstrumentPlan.EmptyText(none.Signal, null, french: false));

        // player 2 has his own, untouched by player 1's
        Assert.Equal("players.2.bombs", InstrumentPlan.Pick(semantics.Signals, 2, "bombs")!.Id);
        Assert.Equal(3, Assert.Single(semantics.Readings, r => r.Signal.Id == "players.2.bombs").Value);

        // the shot level carries the game's own word
        var shot = Assert.Single(semantics.Apply("0xFF8337", "WEAPON_STATE", 1, 255));
        Assert.Equal("players.1.power.level", shot.Signal.Id);
        Assert.Equal("Boosted", shot.Text);
    }

    // ── the DMD only speaks when a reserve runs dry ──────────────────────────

    [Fact]
    public void A_reserve_that_runs_dry_is_announced_once_it_is_empty()
    {
        var bombs = Signal("players.1.bombs", unit: "bomb", shortName: "BOMB");

        Assert.Null(InstrumentPlan.RunDryNotice(new SemanticReading(bombs, 1, ""), french: false));
        var notice = InstrumentPlan.RunDryNotice(new SemanticReading(bombs, 0, ""), french: false);
        Assert.Equal(("BOMB", "EMPTY"), notice!.Value);
    }

    [Fact]
    public void The_second_players_notice_says_whose_it_is()
    {
        var ammo = Signal("players.2.ammo", player: 2, shortName: "AMMO");

        Assert.Equal(("AMMO P2", "RECHARGEZ"), InstrumentPlan.RunDryNotice(new SemanticReading(ammo, 0, ""), french: true)!.Value);
    }

    [Fact]
    public void A_timer_reaching_zero_is_not_announced()
    {
        Assert.Null(InstrumentPlan.RunDryNotice(new SemanticReading(Signal("players.1.stun.timeout", unit: "frame"), 0, ""), french: false));
        Assert.Null(InstrumentPlan.RunDryNotice(new SemanticReading(Signal("players.1.weapon.id", kind: "enum"), 0, ""), french: false));
    }
}
