using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Inventory;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Umbra.Common;
using Umbra.Widgets;
using Umbra.Widgets.MenuPopup;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace FoodUmbra;

public sealed class FoodWidget : StandardToolbarWidget
{
    private IGameInventory GameInventory =>
        (IGameInventory)Framework.DalamudPlugin.GetService(typeof(IGameInventory))!;

    private IDataManager DataManager =>
        (IDataManager)Framework.DalamudPlugin.GetService(typeof(IDataManager))!;

    private readonly MenuPopup Popup = new();

    public FoodWidget()
    {
        Id = "FoodUmbra";
        IconId = 60146;
        Priority = 0;

        Popup.OnPopupOpen += RebuildMenu;
    }

    public override void OnUnload()
    {
        Popup.OnPopupOpen -= RebuildMenu;
        Popup.Dispose();

        base.OnUnload();
    }

    protected override void OnClick()
    {
        Popup.Toggle();
    }

    private void RebuildMenu()
    {
        Popup.Clear();

        var entries = ScanFood();

        if (entries.Count == 0)
        {
            Popup.Add(new MenuPopup.Button
            {
                Text = "No food in inventory",
                IsDisabled = true,
            });

            return;
        }

        foreach (var entry in entries)
        {
            var item = DataManager.GetExcelSheet<Item>()
                .GetRow(entry.BaseItemId);

            var menuItem = new MenuPopup.Button
            {
                Text = $"{(entry.IsHighQuality ? "★ " : "")}{item.Name}",
                AltText = $"×{entry.Quantity}",
                IconId = (uint)item.Icon,
                Tooltip = BuildFoodTooltip(item, entry),
            };

            menuItem.OnClick = () =>
            {
                var inventoryContext = AgentInventoryContext.Instance();

                if (inventoryContext == null)
                    return;

                inventoryContext->UseItem(
                    entry.ActualItemId,
                    entry.InventoryType,
                    entry.InventorySlot);
            };

            Popup.Add(menuItem);
        }
    }

    private List<FoodEntry> ScanFood()
    {
        var results = new Dictionary<(uint BaseItemId, bool IsHq), FoodEntry>();

        foreach (var inventoryItem in GameInventory.GetInventoryItems())
        {
            if (inventoryItem.ItemId == 0)
                continue;

            if (inventoryItem.Item == null)
                continue;

            if (inventoryItem.Item.Value.FilterGroup != 5)
                continue;

            var key = (inventoryItem.BaseItemId, inventoryItem.IsHq);

            if (results.TryGetValue(key, out var existing))
            {
                results[key] = existing with
                {
                    Quantity = existing.Quantity + inventoryItem.Quantity,
                };
            }
            else
            {
                results[key] = new FoodEntry(
                    BaseItemId: inventoryItem.BaseItemId,
                    ActualItemId: inventoryItem.ItemId,
                    IsHighQuality: inventoryItem.IsHq,
                    Quantity: inventoryItem.Quantity,
                    InventoryType: inventoryItem.InventorySlot.Type,
                    InventorySlot: inventoryItem.InventorySlot.Slot);
            }
        }

        return results.Values
            .OrderByDescending(x => x.IsHighQuality)
            .ThenBy(x =>
            {
                var item = DataManager.GetExcelSheet<Item>()
                    .GetRow(x.BaseItemId);

                return item.Name.ToString();
            })
            .ToList();
    }

    private string BuildFoodTooltip(Item item, FoodEntry entry)
    {
        var foodSheet = DataManager.GetExcelSheet<ItemFood>();
        var food = foodSheet.GetRow(entry.BaseItemId);

        var lines = new List<string>
        {
            item.Name.ToString(),
            "Meal",
        };

        foreach (var param in food.Params)
        {
            var baseParam = param.BaseParam.Value;

            if (baseParam.RowId == 0)
                continue;

            var value = entry.IsHighQuality
                ? param.ValueHQ
                : param.Value;

            var max = entry.IsHighQuality
                ? param.MaxHQ
                : param.Max;

            if (value == 0 && max == 0)
                continue;

            var paramName = baseParam.Name.ToString();

            if (string.IsNullOrWhiteSpace(paramName))
                continue;

            var effect = param.IsRelative
                ? $"+{value}%"
                : $"+{value}";

            if (max > 0)
                effect += $" (Max {max})";

            lines.Add($"{paramName}: {effect}");
        }

        if (food.EXPBonusPercent > 0)
            lines.Add($"EXP Bonus: +{food.EXPBonusPercent}%");

        return string.Join("\n", lines);
    }

    private readonly record struct FoodEntry(
        uint BaseItemId,
        uint ActualItemId,
        bool IsHighQuality,
        uint Quantity,
        InventoryType InventoryType,
        uint InventorySlot);
}
