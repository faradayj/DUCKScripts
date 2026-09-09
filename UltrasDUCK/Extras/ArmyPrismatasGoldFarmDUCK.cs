/*
name: Army Prismatas Gold Farm DUCK
description: Seven-player CoreDUCK Army gold farm for Elemental Binding in /archmage.
tags: gold, prismatas, elemental binding, seven-player, army, coreduck
*/

//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreAdvanced.cs
//cs_include Scripts/DUCKScripts/UltrasDUCK/CoreDUCK.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Skua.Core.Interfaces;
using Skua.Core.Options;

#nullable enable

public class ArmyPrismatasGoldFarmDUCK
{
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private static readonly CoreDUCK Duck = new();

    private const string LogPrefix = "ArmyPrismatasGoldFarmDUCK";
    private const string SyncFileName = "ArmyPrismatasGoldFarmDUCK.sync";
    private const string MapName = "archmage";
    private const string SafeCell = "Enter";
    private const string SafePad = "Spawn";
    private const string FightCell = "r2";
    private const string FightPad = "Left";
    private const string ElementalBinding = "Elemental Binding";
    private const string GoldVoucher100k = "Gold Voucher 100k";
    private const string GoldVoucher500k = "Gold Voucher 500k";
    private const string VoucherMap = "alchemyacademy";
    private const int VoucherShopId = 2036;
    private const int VoucherMaxStack = 300;
    private const int BindingMaxStack = 2500;
    private const int GoldVoucher100kPrice = 100_000;
    private const int GoldVoucher500kPrice = 500_000;
    private const int GoldCap = 100_000_000;
    private const int MinimumLevel = 80;
    private const int FirstTargetMapId = 1;
    private const int SecondTargetMapId = 2;
    private const int FarmPollDelay = 100;
    private const int RespawnPollDelay = 500;
    private const int TargetArmySize = 7;
    private const int DefaultPrivateRoomNumber = 1245;
    private const int BindingSellQuantity = 100;
    private const bool FarmGoldVouchers = false;
    private const bool EnableAntiLag = true;
    private const bool UsePotions = true;
    private const bool UseEnhancements = true;

    private string playerAlias = string.Empty;
    private int privateRoomNumber = DefaultPrivateRoomNumber;
    private bool masterMode;
    private UltraRunResult runResult = UltraRunResult.Failed;
    private DuckAssignmentResult? currentAssignResult;

    private bool antiLagApplied;
    private bool originalLagKiller;
    private bool originalHidePlayers;
    private bool originalDisableSelfAnimation;
    private bool originalDisableMonsterAnimation;
    private bool originalDisableSkillAnimation;
    private bool originalDisableDamageStrobe;
    private bool originalMonstersHidden;

    public string OptionsStorage = "ArmyPrismatasGoldFarmDUCK";
    public bool DontPreconfigure = true;
    public List<IOption> Options = new();

    private static readonly DuckSlotRequirement[] SevenPlayerSlotRequirements = new[]
    {
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchFiend", "Legion Revenant" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "StoneCrusher" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "ArchPaladin" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Lord of Order" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Shaman" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Bard" } },
        new DuckSlotRequirement { CandidateClasses = new[] { "Verus DoomKnight" } }
    };

    public void ScriptMain(IScriptInterface Bot)
    {
        Bot.Skills.Stop();
        Bot.Options.InfiniteRange = true;
        Bot.Options.AggroMonsters = true;

        try
        {
            ApplyAntiLag();
            Run();
        }
        finally
        {
            Duck.StopSkillEngine();
            RestoreAntiLag();
            masterMode = false;
        }
    }

    public UltraRunResult Run(bool isMasterMode = false, int masterRoomNumber = DefaultPrivateRoomNumber)
    {
        masterMode = isMasterMode;
        privateRoomNumber = masterRoomNumber;
        runResult = UltraRunResult.Failed;

        try
        {
            ExecuteScript();
        }
        finally
        {
            Duck.StopSkillEngine();
            masterMode = false;
        }

        return runResult;
    }

    private void ApplyAntiLag()
    {
        if (!EnableAntiLag)
            return;

        originalLagKiller = Bot.Options.LagKiller;
        originalHidePlayers = Bot.Options.HidePlayers;
        originalDisableSelfAnimation = Bot.Lite.DisableSelfAnimation;
        originalDisableMonsterAnimation = Bot.Lite.DisableMonsterAnimation;
        originalDisableSkillAnimation = Bot.Lite.DisableSkillAnimation;
        originalDisableDamageStrobe = Bot.Lite.DisableDamageStrobe;
        originalMonstersHidden = Bot.Flash.GetGameObject<bool>(
            "ui.monsterIcon.redX.visible"
        );
        antiLagApplied = true;

        Bot.Options.LagKiller = true;
        Bot.Options.HidePlayers = true;
        Bot.Lite.DisableSelfAnimation = true;
        Bot.Lite.DisableMonsterAnimation = true;
        Bot.Lite.DisableSkillAnimation = true;
        Bot.Lite.DisableDamageStrobe = true;

        if (!originalMonstersHidden)
            Bot.Flash.CallGameFunction("world.toggleMonsters");
    }

    private void RestoreAntiLag()
    {
        if (!antiLagApplied)
            return;

        Bot.Options.LagKiller = originalLagKiller;
        Bot.Options.HidePlayers = originalHidePlayers;
        Bot.Lite.DisableSelfAnimation = originalDisableSelfAnimation;
        Bot.Lite.DisableMonsterAnimation = originalDisableMonsterAnimation;
        Bot.Lite.DisableSkillAnimation = originalDisableSkillAnimation;
        Bot.Lite.DisableDamageStrobe = originalDisableDamageStrobe;

        bool monstersHidden = Bot.Flash.GetGameObject<bool>(
            "ui.monsterIcon.redX.visible"
        );
        if (monstersHidden != originalMonstersHidden)
            Bot.Flash.CallGameFunction("world.toggleMonsters");

        antiLagApplied = false;
    }

    private void ExecuteScript()
    {
        if (!masterMode)
            privateRoomNumber = DefaultPrivateRoomNumber;

        if (!Duck.ValidatePrivateRoomNumber(privateRoomNumber))
            return;

        DuckAssignmentResult? assignResult = Duck.AutoAssignAndEquip(
            SyncFileName,
            SevenPlayerSlotRequirements,
            armySize: TargetArmySize,
            timeoutSeconds: 120,
            allowDuplicates: false
        );

        if (assignResult == null)
            return;

        currentAssignResult = assignResult;

        if (!Duck.StartArmySyncDynamic(SyncFileName, assignResult.DiscoveredPlayers))
            return;

        ClassPreset preset = assignResult.Preset;
        if (string.Equals(assignResult.RoleName, "Shaman", StringComparison.OrdinalIgnoreCase))
        {
            preset = Duck.Shaman(farmMode: true);
            preset.HelmEnhancement = HelmSpecial.Examen;
        }

        playerAlias = $"Player {assignResult.PlayerNumber} ({preset.ClassName})";

        if (!ValidateAccess(preset))
            return;

        Duck.FileLog($"Started as {playerAlias} in room {privateRoomNumber}.", LogPrefix);
        Core.Logger($"{LogPrefix} started as {playerAlias} in room {privateRoomNumber}.");

        RegisterBindingDrop();

        if (!Prepare(preset) || !Sync("SETUP_DONE"))
            return;

        bool armyComplete;
        if (!RunSafeRoomStage(preset, 0, out armyComplete))
            return;

        int farmCycle = 1;
        while (!Bot.ShouldExit && !armyComplete)
        {
            if (!FarmBindings(preset, farmCycle))
                return;

            if (!Sync($"CYCLE_{farmCycle}_SELL_READY"))
                return;

            if (!RunSafeRoomStage(preset, farmCycle, out armyComplete))
                return;

            farmCycle++;
        }

        if (Bot.ShouldExit || !armyComplete)
            return;

        MoveToHouse();
        if (Bot.ShouldExit || !Sync("PARKED"))
            return;

        runResult = UltraRunResult.Completed;

        if (assignResult.IsLeader)
        {
            Bot.Sleep(1000);
            Duck.ClearAllSyncFiles(SyncFileName);
            Duck.FileLog("Session leader cleared all sync files.", LogPrefix);
            Core.Logger($"{LogPrefix} session leader cleared all sync files.");
        }
    }

    private bool ValidateAccess(ClassPreset preset)
    {
        if (Bot.Player.Level >= MinimumLevel)
            return true;

        return Fatal(
            $"{LogPrefix} requires level {MinimumLevel} or higher for {playerAlias} ({preset.ClassName}).",
            "ValidateAccess"
        );
    }

    private bool Prepare(ClassPreset preset)
    {
        Core.Logger($"{LogPrefix} {playerAlias} starting setup.");

        Duck.EquipClass(preset);
        if (Bot.ShouldExit)
            return false;

        if (UseEnhancements)
        {
            Duck.PrepareEnhancements(
                preset.BaseEnhancement,
                preset.CapeEnhancement,
                preset.HelmEnhancement,
                preset.WeaponEnhancement,
                weaponFallbacks: preset.WeaponEnhancementFallbacks
            );
        }

        if (UsePotions)
        {
            Duck.PreparePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        if (Bot.ShouldExit)
            return false;

        if (!PrepareBankItems())
            return false;

        Duck.FileLog($"{playerAlias} finished setup.", LogPrefix);
        Core.Logger($"{LogPrefix} {playerAlias} finished setup.");
        return true;
    }

    private bool PrepareBankItems()
    {
        if (Bot.Flash.GetGameObject("ui.mcPopup.currentLabel") != "\"Bank\"")
            Bot.Bank.Open();

        Bot.Bank.Load(waitForLoad: false);
        Bot.Bank.Loaded = true;

        if (!MoveBankedItemToInventory(ElementalBinding))
            return false;

        return !FarmGoldVouchers
            || (
                MoveBankedItemToInventory(GoldVoucher100k)
                && MoveBankedItemToInventory(GoldVoucher500k)
            );
    }

    private bool MoveBankedItemToInventory(string itemName)
    {
        int bankQuantity = Bot.Bank.GetQuantity(itemName);
        if (bankQuantity <= 0)
            return true;

        if (!Bot.Inventory.Contains(itemName) && !Core.HasSpace)
        {
            return Fatal(
                $"{itemName} cannot be moved from bank because the inventory is full.",
                "PrepareBankItems"
            );
        }

        int inventoryQuantity = Bot.Inventory.GetQuantity(itemName);
        int expectedQuantity = inventoryQuantity + bankQuantity;

        Bot.Bank.EnsureToInventory(itemName, loadBank: false);
        Bot.Wait.ForTrue(
            () => Bot.Inventory.GetQuantity(itemName) >= expectedQuantity,
            20
        );

        int finalQuantity = Bot.Inventory.GetQuantity(itemName);
        if (finalQuantity < expectedQuantity)
        {
            return Fatal(
                $"{itemName} could not be moved completely from bank. Inventory quantity: {finalQuantity}/{expectedQuantity}.",
                "PrepareBankItems"
            );
        }

        Core.Logger(
            $"{itemName} moved from bank. Inventory quantity: {finalQuantity}.",
            "PrepareBankItems"
        );
        return true;
    }

    private void RegisterBindingDrop()
    {
        if (!Bot.Drops.ToPickup.Contains(ElementalBinding))
            Core.AddDrop(ElementalBinding);

        if (!Bot.Drops.Enabled)
            Bot.Drops.Start();
    }

    private bool FarmBindings(ClassPreset preset, int farmCycle)
    {
        Duck.JoinRoom(MapName, privateRoomNumber, FightCell, FightPad);
        Core.Jump(FightCell, FightPad);
        Bot.Options.AggroMonsters = true;

        Duck.StartSkillEngine(
            preset.Skills,
            playerAlias,
            false,
            LogPrefix,
            preset.SkillMode,
            maintainedPotion: preset.CombatPotion
        );
        Core.Logger(
            $"{LogPrefix} {playerAlias} started Elemental Binding cycle {farmCycle}."
        );

        string readySignal = $"CYCLE_{farmCycle}_BINDING_READY";
        bool readySignalSent = false;
        bool deathLogged = false;

        while (!Bot.ShouldExit)
        {
            if (!Bot.Player.Alive)
            {
                if (!deathLogged)
                {
                    Core.Logger($"{LogPrefix} {playerAlias} died during cycle {farmCycle}.");
                    deathLogged = true;
                }

                Bot.Sleep(RespawnPollDelay);
                continue;
            }

            if (deathLogged)
            {
                Core.Logger($"{LogPrefix} {playerAlias} respawned during cycle {farmCycle}.");
                deathLogged = false;
                Core.Jump(FightCell, FightPad);
            }

            if (!readySignalSent && ReachedBindingSellQuantity())
            {
                if (!Duck.SendArmySignal(readySignal))
                    return false;

                readySignalSent = true;
                Core.Logger(
                    $"{LogPrefix} {playerAlias} reached {BindingSellQuantity} Elemental Binding for cycle {farmCycle} and is continuing to help."
                );
            }

            if (readySignalSent && AllPlayersSignaled(readySignal))
                break;

            int targetMapId = GetCurrentTargetMapId();
            if (targetMapId > 0)
                Duck.MaintainTarget(targetMapId);

            Bot.Sleep(FarmPollDelay);
        }

        StopCombat();

        if (Bot.ShouldExit)
            return false;

        return true;
    }

    private bool ReachedBindingSellQuantity() =>
        Bot.Inventory.GetQuantity(ElementalBinding) >= BindingSellQuantity;

    private int GetCurrentTargetMapId()
    {
        if (Duck.IsMonsterAlive(FirstTargetMapId))
            return FirstTargetMapId;

        return Duck.IsMonsterAlive(SecondTargetMapId)
            ? SecondTargetMapId
            : 0;
    }

    private bool RunSafeRoomStage(
        ClassPreset preset,
        int farmCycle,
        out bool armyComplete
    )
    {
        armyComplete = false;
        StopCombat();
        MoveToSafeRoom();

        if (!IsLocalGoalComplete())
        {
            if (FarmGoldVouchers && Bot.Player.Gold >= GoldCap)
            {
                PurchaseGoldVouchers();

                if (Bot.ShouldExit)
                    return false;

                MoveToSafeRoom();
            }

            if (Bot.Player.Gold < GoldCap)
            {
                if (!SellBindings(farmCycle))
                    return false;

                if (FarmGoldVouchers)
                    PurchaseGoldVouchers();

                if (Bot.ShouldExit)
                    return false;
            }
        }

        MoveToSafeRoom();

        bool localComplete = IsLocalGoalComplete();
        string completionSignal = $"CYCLE_{farmCycle}_COMPLETE";
        if (localComplete && !Duck.SendArmySignal(completionSignal))
            return false;

        if (!Sync($"CYCLE_{farmCycle}_SAFE_DONE"))
            return false;

        armyComplete = AllPlayersSignaled(completionSignal);
        if (armyComplete)
        {
            Core.Logger(
                $"{LogPrefix} confirmed every active account completed the gold objective."
            );
            return true;
        }

        if (UsePotions)
        {
            Duck.UsePotions(
                preset.Tonic,
                preset.Elixir,
                preset.CombatPotion
            );
        }

        Bot.Options.AggroMonsters = true;
        return !Bot.ShouldExit;
    }

    private void MoveToSafeRoom()
    {
        if (string.Equals(Bot.Map.Name, MapName, StringComparison.OrdinalIgnoreCase))
        {
            Core.Jump(SafeCell, SafePad);
            return;
        }

        Duck.JoinRoom(MapName, privateRoomNumber, SafeCell, SafePad);
    }

    private bool SellBindings(int farmCycle)
    {
        int quantityBefore = Bot.Inventory.GetQuantity(ElementalBinding);
        if (quantityBefore <= 0)
        {
            Core.Logger(
                $"{ElementalBinding} inventory is already clear for cycle {farmCycle}.",
                "SellBindings"
            );
            return true;
        }

        Core.SellItem(ElementalBinding, all: true);
        int quantityAfter = Bot.Inventory.GetQuantity(ElementalBinding);

        if (quantityAfter <= 0)
        {
            Core.Logger(
                $"Sold {quantityBefore} {ElementalBinding} for cycle {farmCycle}.",
                "SellBindings"
            );
            return true;
        }

        return Fatal(
            $"{ElementalBinding} failed to sell completely. Remaining quantity: {quantityAfter}.",
            "SellBindings"
        );
    }

    private void PurchaseGoldVouchers()
    {
        BuyAffordableVoucher(
            GoldVoucher100k,
            GoldVoucher100kPrice
        );

        if (GetVoucherQuantity(GoldVoucher100k) < VoucherMaxStack)
            return;

        BuyAffordableVoucher(
            GoldVoucher500k,
            GoldVoucher500kPrice
        );
    }

    private void BuyAffordableVoucher(string voucherName, int price)
    {
        int quantityBefore = GetVoucherQuantity(voucherName);
        int missing = VoucherMaxStack - quantityBefore;
        int affordable = Bot.Player.Gold / price;
        int buyQuantity = Math.Min(missing, affordable);

        if (buyQuantity <= 0)
            return;

        Core.BuyItem(VoucherMap, VoucherShopId, voucherName, buyQuantity);

        int quantityAfter = GetVoucherQuantity(voucherName);
        Core.Logger(
            $"{voucherName}: {quantityAfter}/{VoucherMaxStack} after buying {Math.Max(0, quantityAfter - quantityBefore)}.",
            "PurchaseGoldVouchers"
        );
    }

    private int GetVoucherQuantity(string voucherName) =>
        Bot.Inventory.GetQuantity(voucherName) + Bot.Bank.GetQuantity(voucherName);

    private bool IsLocalGoalComplete()
    {
        if (Bot.Player.Gold < GoldCap)
            return false;

        return !FarmGoldVouchers
            || (
                GetVoucherQuantity(GoldVoucher100k) >= VoucherMaxStack
                && GetVoucherQuantity(GoldVoucher500k) >= VoucherMaxStack
            );
    }

    private bool AllPlayersSignaled(string signal)
    {
        for (int playerNumber = 1; playerNumber <= TargetArmySize; playerNumber++)
        {
            if (!Duck.HasArmySignal(signal, playerNumber))
                return false;
        }

        return true;
    }

    private void StopCombat()
    {
        Duck.StopSkillEngine();
        Bot.Combat.CancelTarget();
    }

    private bool Sync(string step)
    {
        Core.Logger($"{LogPrefix} {playerAlias} entering {step}.");

        if (!Duck.SyncArmy(step))
            return false;

        Core.Logger($"{LogPrefix} {playerAlias} continued from {step}.");
        return true;
    }

    private void MoveToHouse()
    {
        Bot.Send.Packet($"%xt%zm%house%1%{Bot.Player.Username}%");
        Bot.Wait.ForMapLoad("house");
    }

    private bool Fatal(string message, string caller)
    {
        Core.Logger(message, caller, messageBox: true, stopBot: true);
        return false;
    }
}
