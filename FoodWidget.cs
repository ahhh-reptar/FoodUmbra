using System.Collections.Generic;
using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
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

        var food = new Dictionary<(uint ItemId, bool IsHq), FoodEntry>();

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

                var itemId = inventoryItem.BaseItemId;
                var isHq = inventoryItem.IsHq;

                var itemResult = itemSheet.GetRowOrDefault(itemId);

                if (!itemResult.HasValue)
                    continue;

                var item = itemResult.Value;

                // Item.FilterGroup 5 = Meal.
                if (item.FilterGroup != 5)
                    continue;

                var key = (itemId, isHq);

                if (food.TryGetValue(key, out var existing))
                {
                    existing.Quantity += inventoryItem.Quantity;
                }
                else
                {
                    food[key] = new FoodEntry(
                        itemId,
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
            var itemResult = itemSheet.GetRowOrDefault(entry.ItemId);

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

            menuItem.OnClick = () => UseFood(entry);

            Popup.Add(menuItem);
        }
    }

    private static void UseFood(FoodEntry entry)
    {
        var agent = AgentInventoryContext.Instance();

        if (agent == null)
            return;

        agent->UseItem(
            entry.ItemId,
            entry.InventoryType,
            entry.InventorySlot
        );
    }

    private sealed class FoodEntry(
        uint itemId,
        bool isHighQuality,
        int quantity,
        InventoryType inventoryType,
        uint inventorySlot
    )
    {
        public uint ItemId { get; } = itemId;
        public bool IsHighQuality { get; } = isHighQuality;
        public int Quantity { get; set; } = quantity;
        public InventoryType InventoryType { get; } = inventoryType;
        public uint InventorySlot { get; } = inventorySlot;
    }
}
