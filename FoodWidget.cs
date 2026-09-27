using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Umbra.Common;
using Umbra.Widgets;

namespace FoodUmbra.Widgets;

[ToolbarWidget(
    "FoodWidget",
    "Food",
    "Browse food in your inventory."
)]
public unsafe class FoodWidget(
    WidgetInfo info,
    string? guid = null,
    Dictionary<string, object>? configValues = null
) : StandardToolbarWidget(info, guid, configValues)
{
    protected override StandardWidgetFeatures Features =>
        StandardWidgetFeatures.Text |
        StandardWidgetFeatures.Icon |
        StandardWidgetFeatures.CustomizableIcon;

    protected override uint DefaultGameIconId => 60146;

    public override MenuPopup Popup { get; } = new();

    private IGameInventory GameInventory =>
        (IGameInventory)Framework.DalamudPlugin.GetService(typeof(IGameInventory))!;

    private IDataManager DataManager =>
        (IDataManager)Framework.DalamudPlugin.GetService(typeof(IDataManager))!;

    protected override void OnLoad()
    {
        SetText("Food");
        Popup.OnPopupOpen += RebuildMenu;
        RebuildMenu();
    }

    protected override void OnDraw() {}

    protected override void OnUnload()
    {
        Popup.OnPopupOpen -= RebuildMenu;
    }

    private void RebuildMenu()
    {
        Popup.Clear();

        var itemSheet = DataManager.GetExcelSheet<Item>();
        var foodSheet = DataManager.GetExcelSheet<ItemFood>();

        var food = new Dictionary<(uint BaseItemId, bool IsHq), FoodEntry>();

        foreach (var (inventoryType, ffxivInventoryType) in new[]
        {
            (GameInventoryType.Inventory1, InventoryType.Inventory1),
            (GameInventoryType.Inventory2, InventoryType.Inventory2),
            (GameInventoryType.Inventory3, InventoryType.Inventory3),
            (GameInventoryType.Inventory4, InventoryType.Inventory4)
        })
        {
            var items = GameInventory.GetInventoryItems(inventoryType);

            for (var slot = 0; slot < items.Length; slot++)
            {
                var inventoryItem = items[slot];

                if (inventoryItem.IsEmpty || inventoryItem.Quantity == 0)
                    continue;

                var baseItemId = inventoryItem.BaseItemId;
                var actualItemId = inventoryItem.ItemId;
                var isHq = inventoryItem.IsHq;

                var itemResult = itemSheet.GetRowOrDefault(baseItemId);

                if (!itemResult.HasValue)
                    continue;

                var item = itemResult.Value;

                // Item.FilterGroup 5 = Meal.
                if (item.FilterGroup != 5)
                    continue;

                var key = (baseItemId, isHq);

                if (food.TryGetValue(key, out var existing))
                {
                    existing.Quantity += inventoryItem.Quantity;
                }
                else
                {
                    food[key] = new FoodEntry(
                        baseItemId,
                        actualItemId,
                        isHq,
                        inventoryItem.Quantity,
                        ffxivInventoryType,
                        (uint)slot
                    );
                }
            }
        }

        foreach (var entry in food.Values)
        {
            var itemResult = itemSheet.GetRowOrDefault(entry.BaseItemId);

            if (!itemResult.HasValue)
                continue;

            var item = itemResult.Value;

            var displayName = entry.IsHighQuality
                ? $"★ {item.Name}"
                : item.Name.ToString();

            var menuItem = new MenuPopup.Button(displayName)
            {
                Icon = (uint)item.Icon,
                AltText = $"×{entry.Quantity}"
            };
            
            menuItem.Node.Tooltip = BuildFoodTooltip(item, entry, foodSheet);

            menuItem.OnClick = () => UseFood(entry);

            Popup.Add(menuItem);
        }
    }

    private string BuildFoodTooltip(
        Item item,
        FoodEntry entry,
        ExcelSheet<ItemFood> foodSheet)
    {
        var itemActionId = item.ItemAction.RowId;
    
        if (itemActionId == 0)
            return item.Name.ToString();
    
        var itemActionSheet = DataManager.GetExcelSheet<ItemAction>();
    
        if (itemActionSheet == null)
            return item.Name.ToString();
    
        var actionResult = itemActionSheet.GetRowOrDefault(itemActionId);
    
        if (!actionResult.HasValue)
            return item.Name.ToString();
    
        var action = actionResult.Value;
    
        if (action.Data.Count <= 1)
            return item.Name.ToString();
    
        var foodRowId = (uint)action.Data[1];
    
        if (foodRowId == 0)
            return item.Name.ToString();
    
        var foodResult = foodSheet.GetRowOrDefault(foodRowId);
    
        if (!foodResult.HasValue)
            return item.Name.ToString();
    
        var food = foodResult.Value;
    
        var lines = new List<string>
        {
            item.Name.ToString(),
            $"Item Level: {item.LevelItem.RowId}"
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
    
            var name = baseParam.Name.ToString();
    
            if (string.IsNullOrWhiteSpace(name))
                continue;
    
            var effect = param.IsRelative
                ? $"+{value}%"
                : $"+{value}";
    
            if (max > 0)
                effect += $" (Max {max})";
    
            lines.Add($"{name}: {effect}");
        }
    
        return string.Join("\n", lines);
    }

    private static void UseFood(FoodEntry entry)
    {
        var agent = AgentInventoryContext.Instance();

        if (agent == null)
            return;

        agent->UseItem(
            entry.ActualItemId,
            entry.InventoryType,
            entry.InventorySlot
        );
    }

    private sealed class FoodEntry(
        uint baseItemId,
        uint actualItemId,
        bool isHighQuality,
        int quantity,
        InventoryType inventoryType,
        uint inventorySlot
    )
    {
        public uint BaseItemId { get; } = baseItemId;
        public uint ActualItemId { get; } = actualItemId;
        public bool IsHighQuality { get; } = isHighQuality;
        public int Quantity { get; set; } = quantity;
        public InventoryType InventoryType { get; } = inventoryType;
        public uint InventorySlot { get; } = inventorySlot;
    }
}
