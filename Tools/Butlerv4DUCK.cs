/*
name: Butlerv4 DUCK (TCP)
description: Follows a leader via Goto/TCP with CoreDUCK asynchronous skill engine integration.
tags: butler, follow, goto, tcp, coreduck, duck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/CoreFarms.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Skua.Core.Interfaces;
using Skua.Core.Options;

public class Butlerv4DUCK
{
    private static IScriptInterface Bot => IScriptInterface.Instance;
    private static CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private static CoreAdvanced Adv
    {
        get => _Adv ??= new CoreAdvanced();
        set => _Adv = value;
    }
    private static CoreAdvanced _Adv;

    public bool DontPreconfigure = true;
    public string OptionsStorage = "Butler1";

    public List<IOption> Options = new()
    {
        new Option<string>("Leader1Name", "Leader 1 Name", "Name of leader 1.", ""),
        new Option<string>("Leader1Butlers", "Butlers For Leader 1", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader2Name", "Leader 2 Name", "Name of leader 2.", ""),
        new Option<string>("Leader2Butlers", "Butlers For Leader 2", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader3Name", "Leader 3 Name", "Name of leader 3.", ""),
        new Option<string>("Leader3Butlers", "Butlers For Leader 3", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<string>("Leader4Name", "Leader 4 Name", "Name of leader 4.", ""),
        new Option<string>("Leader4Butlers", "Butlers For Leader 4", "Comma-separated butler account names. Example: acc1,acc2,acc3", ""),
        new Option<bool>("AutoEnhance", "Auto Enhance", "Automatically enhance equipped class on startup using CoreDUCK.", true),
        new Option<bool>("UseGoto", "Use Goto", "Use Goto to follow instead of direct Join+Jump.", true),
        CoreBots.Instance.SkipOptions,
    };

    private const string LogPrefix = "[Butler DUCK]";
    private string playerName = string.Empty;
    private string currentClassName = string.Empty;
    private ClassPreset? currentPreset;
    private volatile bool _gotoPending;
    private DateTime _lastGotoTime = DateTime.MinValue;
    private const int GotoMinIntervalMs = 500;
    private bool _skillEngineActive = false;

    // ── TCP state ────────────────────────────────────────────────────
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private StreamReader? _streamReader;
    private string _tcpMap = "";
    private string _tcpRoom = "";
    private string _tcpCell = "";
    private string _tcpPad = "";
    private bool _tcpInCombat = false;
    private bool _tcpHasTarget = false;
    private bool _lockedZone = false;
    private bool _roomFull = false;
    private bool _pvpZone = false;
    private bool _gotoIgnored = false;
    private bool _differentServer = false;
    private bool _isParked = false;
    private bool _houseJoined = false;
    private bool _leaderPortLookupFailedLogged = false;

    public void ScriptMain(IScriptInterface bot)
    {
        Core.SetOptions(disableClassSwap: true);

        Bot.Skills.Stop();
        Bot.Options.AttackWithoutTarget = false;
        Bot.Options.AggroAllMonsters = false;
        Bot.Options.AggroMonsters = false;

        Bot.Events.ExtensionPacketReceived += ChatListener;

        string myUsername = Bot.Player.Username ?? "";

        for (int i = 1; i <= 4; i++)
        {
            string butlerList = Bot.Config!.Get<string>($"Leader{i}Butlers") ?? "";
            var butlers = butlerList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (butlers.Any(b => string.Equals(b, myUsername, StringComparison.OrdinalIgnoreCase)))
            {
                playerName = Bot.Config!.Get<string>($"Leader{i}Name") ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(playerName))
        {
            Core.Logger($"This account is not assigned to any leader's butler list.", messageBox: true, stopBot: true);
            return;
        }

        ConnectToLeader();

        currentPreset = ResolveCurrentClassPreset();
        currentClassName = currentPreset.ClassName;

        if (Bot.Config!.Get<bool>("AutoEnhance"))
        {
            Core.Logger($"{LogPrefix} Auto-enhancing class '{currentClassName}' via CoreDUCK.");
            Duck.PrepareEnhancements(
                currentPreset.BaseEnhancement,
                currentPreset.CapeEnhancement,
                currentPreset.HelmEnhancement,
                currentPreset.WeaponEnhancement,
                weaponFallbacks: currentPreset.WeaponEnhancementFallbacks
            );
        }

        DontAttack();

        try
        {
            FollowLeader();
        }
        finally
        {
            StopSkillEngine();
            Bot.Events.ExtensionPacketReceived -= ChatListener;
            Disconnect();
        }
    }

    private ClassPreset ResolveCurrentClassPreset()
    {
        string className = Bot.Player.CurrentClass?.Name ?? "";
        string normalized = className.Trim().ToLowerInvariant();

        return normalized switch
        {
            "legion revenant" => Duck.LegionRevenant(),
            "archpaladin" => Duck.ArchPaladin(),
            "lord of order" or "lordoforder" => Duck.LordOfOrder(),
            "stonecrusher" or "infinity titan" or "infinitytitan" => Duck.StoneCrusher(),
            "verus doomknight" => Duck.VerusDoomKnight(),
            "void highlord" => Duck.VoidHighlord(),
            "chaos avenger" => Duck.ChaosAvenger(),
            "chaos slayer" or "chaos slayer berserker" or "chaos slayer mystic" or "chaos slayer cleric" or "chaos slayer thief" => Duck.ChaosSlayer(),
            "dragon of time" => Duck.DragonOfTime(),
            "archfiend" => Duck.ArchFiend(),
            "arachnomancer" => Duck.Arachnomancer(),
            "hollowborn vindicator" => Duck.HollowbornVindicator(),
            "shaman" => Duck.Shaman(),
            "lightcaster" => Duck.LightCaster(),
            "quantum chronomancer" => Duck.QuantumChronomancer(),
            "chrono shadowhunter" or "chrono shadowslayer" => Duck.ChronoShadowHunter(),
            "arcana invoker" => Duck.ArcanaInvoker(),
            "scion of flames" => Duck.ScionOfFlames(),
            "guardian" => Duck.Guardian(),
            "oracle" => Duck.Oracle(),
            "bard" => Duck.Bard(),
            "king's echo" or "kings echo" => Duck.KingsEcho(),
            "yami no ronin" => Duck.YamiNoRonin(),
            "abyssal angel's shadow" or "abyssal angel" or "aas" => Duck.AbyssalAngelShadow(),
            "healer" or "healer (rare)" or "acolyte" => Duck.Healer(),
            "imperial chunin" or "chunin" => Duck.ImperialChunin(),
            "alpha doommega" or "alpha omega" or "alphadoommega" or "alphaomega" or "ao" => Duck.AlphaOmega(),
            _ => new ClassPreset
            {
                ClassName = string.IsNullOrWhiteSpace(className) ? "Generic" : className,
                Skills = new[] { 1, 2, 3, 4 },
                SkillMode = SkillEngineMode.Simple
            }
        };
    }

    private void FollowLeader()
    {
        while (!Bot.ShouldExit)
        {
            while (!Bot.ShouldExit && Bot.Player?.Alive != true)
            {
                StopSkillEngine();
                Core.Sleep(250);
            }

            PollTcpData();

            if (_isParked)
            {
                StopSkillEngine();
                _lockedZone = false;
                _roomFull = false;
                _pvpZone = false;
                _gotoIgnored = false;
                _differentServer = false;
                Core.Sleep(5000);
                _isParked = false;
                continue;
            }

            if (_tcp == null || !_tcp.Connected)
            {
                StopSkillEngine();
                EnterSafeState("Leader is offline or unreachable, parking");
                continue;
            }

            if (_differentServer)
            {
                StopSkillEngine();
                EnterSafeState("Leader could not be found. Either in a different server or logged off, parking");
                continue;
            }

            if (_lockedZone)
            {
                StopSkillEngine();
                Core.Logger($"{LogPrefix} Locked zone — tcpMap=[{_tcpMap}] tcpRoom=[{_tcpRoom}]");
                JoinLeaderMap(_tcpMap, _tcpRoom);
                Core.Jump(_tcpCell, _tcpPad);
                _lockedZone = false;
                continue;
            }

            if (_roomFull)
            {
                StopSkillEngine();
                EnterSafeState("Room is full, parking");
                continue;
            }

            if (_pvpZone)
            {
                StopSkillEngine();
                Core.Logger($"{LogPrefix} PvP zone — tcpMap=[{_tcpMap}] tcpRoom=[{_tcpRoom}]");
                JoinLeaderMap(_tcpMap, _tcpRoom);
                Core.Jump(_tcpCell, _tcpPad);
                _pvpZone = false;
                continue;
            }

            if (_gotoIgnored)
            {
                StopSkillEngine();
                if (Bot.Config!.Get<bool>("UseGoto"))
                {
                    Core.Logger("Please disable incognito mode in CBO for your leader or turn on its Goto", messageBox: true, stopBot: true);
                    return;
                }
                _gotoIgnored = false;
                continue;
            }

            if (int.TryParse(_tcpRoom, out int roomNum) && roomNum < 1000)
            {
                StopSkillEngine();
                EnterSafeState($"Leader in room {roomNum}. Please choose a room number higher than 1000, parking");
                continue;
            }

            TryGotoLeader();

            bool inSameCell = IsLeaderInSameCell();
            bool leaderFighting = _tcpInCombat || _tcpHasTarget;

            if (inSameCell && leaderFighting)
            {
                StartSkillEngine();
                Bot.Combat.Attack("*");
            }
            else
            {
                StopSkillEngine();

                if ((Bot.Player?.InCombat == true || Bot.Player?.HasTarget == true) && !inSameCell)
                    QuickDeaggro();
            }

            Core.Sleep(200);
        }
    }

    private void StartSkillEngine()
    {
        if (_skillEngineActive)
            return;

        string activeClass = Bot.Player.CurrentClass?.Name ?? "";
        if (!string.Equals(activeClass, currentClassName, StringComparison.OrdinalIgnoreCase))
        {
            currentPreset = ResolveCurrentClassPreset();
            currentClassName = currentPreset.ClassName;
        }

        currentPreset ??= ResolveCurrentClassPreset();

        Duck.StartSkillEngine(
            currentPreset.Skills,
            roleName: currentClassName,
            taunter: false,
            logPrefix: LogPrefix,
            mode: currentPreset.SkillMode,
            survivalSkill: currentPreset.SurvivalSkill,
            survivalHealthThreshold: currentPreset.SurvivalHealthThreshold
        );

        _skillEngineActive = true;
    }

    private void StopSkillEngine()
    {
        if (!_skillEngineActive)
            return;

        Duck.StopSkillEngine();
        _skillEngineActive = false;
    }

    private void TryGotoLeader()
    {
        if (_gotoPending)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastGotoTime).TotalMilliseconds < GotoMinIntervalMs)
            return;

        if (string.IsNullOrEmpty(_tcpMap))
            return;

        if (!string.IsNullOrEmpty(_tcpCell) &&
            string.Equals(_tcpMap, Bot.Map?.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_tcpCell, Bot.Player?.Cell, StringComparison.OrdinalIgnoreCase))
            return;

        _lastGotoTime = now;
        _gotoPending = true;

        _ = Task.Run(() =>
        {
            try
            {
                StopSkillEngine();

                if (Bot.ShouldExit) return;
                if (Bot.Config!.Get<bool>("UseGoto"))
                    Bot.Player?.Goto(playerName);
                else
                {
                    string map = _tcpMap;
                    string room = _tcpRoom;
                    string cell = _tcpCell;
                    string pad = _tcpPad;

                    if (Bot.ShouldExit) return;
                    JoinLeaderMap(map, room);
                    if (Bot.ShouldExit) return;
                    Core.Jump(cell, pad);
                }
            }
            catch { }
            finally
            {
                _gotoPending = false;
            }
        });
    }

    private void JoinLeaderMap(string map, string room)
    {
        if (string.IsNullOrEmpty(map) || map == "unknown")
        {
            Core.Logger($"{LogPrefix} Invalid map data from leader (map='{map}'), skipping join.");
            return;
        }

        if (string.IsNullOrEmpty(room))
        {
            Core.Logger($"{LogPrefix} Leader room not yet available, waiting for next broadcast.");
            return;
        }

        if (room.Contains('(') || room.Contains(')'))
        {
            Core.Logger($"{LogPrefix} Leader room is a placeholder ('{room}'), map data not ready yet — waiting.");
            return;
        }

        if (int.TryParse(room, out _))
        {
            Core.Join($"{map}-{room}");
            return;
        }

        Core.Logger($"{LogPrefix} Leader room '{room}' is not a number (likely a public/base map), joining directly.");
        Core.Join(map);
    }

    private void ConnectToLeader()
    {
        int port = ReadLeaderPort(playerName);
        if (port < 0)
            return;

        try
        {
            _tcp = new TcpClient();
            _tcp.Connect("127.0.0.1", port);
            _tcp.NoDelay = true;
            _stream = _tcp.GetStream();
            _streamReader = new StreamReader(_stream, Encoding.UTF8);

            string handshake = $"HELLO|{Bot.Player.Username}|{playerName}\n";
            byte[] hb = Encoding.UTF8.GetBytes(handshake);
            _stream.Write(hb, 0, hb.Length);
            _stream.Flush();

            _streamReader.ReadLine();
        }
        catch
        {
            Disconnect();
        }
    }

    private int ReadLeaderPort(string leaderName)
    {
        try
        {
            var pluginType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LeaderButlerSyncv2.LeaderButlerSyncPlugin", false) ?? assembly.GetType("LeaderButlerSyncPlugin", false))
                .FirstOrDefault(type => type != null);

            var method = pluginType?.GetMethod("ReadLeaderPort", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (method?.Invoke(null, new object[] { leaderName }) is int port)
                return port;
        }
        catch (Exception ex)
        {
            if (!_leaderPortLookupFailedLogged)
            {
                Core.Logger($"{LogPrefix} Failed to read leader port from LeaderButlerSyncv2.dll: {ex.Message}");
                _leaderPortLookupFailedLogged = true;
            }
        }

        if (!_leaderPortLookupFailedLogged)
        {
            Core.Logger($"{LogPrefix} LeaderButlerSyncv2.dll is not loaded. Put it in Skua/plugins and restart your clients.");
            _leaderPortLookupFailedLogged = true;
        }

        return -1;
    }

    private void PollTcpData()
    {
        if (_tcp == null || !_tcp.Connected)
        {
            ConnectToLeader();
            return;
        }

        try
        {
            while (_stream!.DataAvailable)
            {
                string? line = _streamReader!.ReadLine();
                if (line == null)
                {
                    Disconnect();
                    return;
                }
                var parts = line.Split('|');
                if (parts.Length >= 9)
                {
                    _tcpMap = parts[0] ?? "";
                    _tcpRoom = parts[1] ?? "";
                    _tcpCell = parts[2] ?? "";
                    _tcpPad = parts[3] ?? "";
                    _tcpInCombat = parts[6] == "1";
                    _tcpHasTarget = parts[7] == "1";
                }
            }
        }
        catch
        {
            Disconnect();
        }
    }

    private void Disconnect()
    {
        _streamReader?.Close();
        _stream?.Close();
        _tcp?.Close();
        _streamReader = null;
        _stream = null;
        _tcp = null;
    }

    private void DontAttack()
    {
        Bot.Combat.CancelTarget();
        Bot.Options.AttackWithoutTarget = false;
        Bot.Options.AggroAllMonsters = false;
        Bot.Options.AggroMonsters = false;
    }

    private bool IsLeaderInSameCell()
    {
        return !string.IsNullOrEmpty(_tcpCell) &&
               string.Equals(_tcpMap, Bot.Map?.Name, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(_tcpCell, Bot.Player?.Cell, StringComparison.OrdinalIgnoreCase);
    }

    private void QuickDeaggro()
    {
        if (Bot.Player?.Alive != true)
            return;

        if (Bot.Player.InCombat || (Bot.Player.HasTarget && (Bot.Player.Target?.HP ?? 0) > 0))
        {
            DontAttack();
            Bot.Map.Jump(Bot.Player.Cell ?? "Enter", Bot.Player.Pad ?? "Left");
        }
    }

    private void EnterSafeState(string reason)
    {
        if (_isParked)
            return;

        Core.Logger(reason);

        if (!_houseJoined)
        {
            _houseJoined = true;
            if (Bot.House.Items.Any(h => h.Equipped))
            {
                Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
                Bot.Wait.ForMapLoad("house");
            }
            else Core.Join("yulgar-100000");
        }

        _isParked = true;
    }

    private void ChatListener(dynamic packet)
    {
        try
        {
            if (packet == null) return;

            var paramsObj = packet["params"];
            if (paramsObj == null || paramsObj!.type != "str") return;

            dynamic? dataObj = paramsObj!.dataObj;
            if (dataObj == null) return;

            string? cmd = dataObj[0];
            if (string.IsNullOrEmpty(cmd)) return;

            if (cmd == "server")
            {
                string? text = dataObj[2]?.ToString();
                if (!string.IsNullOrEmpty(text) && text.Contains("ignoring goto"))
                    _gotoIgnored = true;
            }
            else if (cmd == "warning")
            {
                string? chat = Convert.ToString(packet);
                if (!string.IsNullOrEmpty(chat))
                {
                    if (chat.Contains("Locked zone") || chat.Contains("not available"))
                        _lockedZone = true;
                    if (chat.Contains("full"))
                        _roomFull = true;
                    if (chat.Contains("PvP zone"))
                        _pvpZone = true;
                    if (chat.Contains("ignoring goto"))
                        _gotoIgnored = true;
                    if (chat.Contains("could not be found"))
                        _differentServer = true;
                }
            }
        }
        catch { }
    }
}
