using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace Redeploy.Server.Scav;

public static class ScavInventoryPolicy
{
    public static bool Extracted(ExitStatus? status) => status is ExitStatus.SURVIVED or ExitStatus.RUNNER;

    /// <summary>Validates the complete graph, returns only descendants of the physical equipment root.</summary>
    public static List<Item> Carried(PmcData profile, Func<MongoId, bool> templateExists)
    {
        var inventory = profile.Inventory ?? throw new InvalidDataException("Missing Scav inventory.");
        var items = inventory.Items?.ToList() ?? throw new InvalidDataException("Missing Scav items.");
        if (items.Count > 8000 || inventory.Equipment == null || inventory.Equipment.Value.IsEmpty) throw new InvalidDataException("Invalid Scav inventory size/root.");
        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var item in items)
            if (item.Id.IsEmpty || !byId.TryAdd(item.Id.ToString(), item) || !templateExists(item.Template))
                throw new InvalidDataException("Invalid or duplicate Scav item.");
        string root = inventory.Equipment.Value.ToString();
        if (!byId.TryGetValue(root, out var equipment) || !string.IsNullOrEmpty(equipment.ParentId))
            throw new InvalidDataException("Invalid Scav equipment root.");
        var carried = new List<Item>();
        foreach (var item in items)
        {
            if (item.Id.ToString() == root) continue;
            var visited = new HashSet<string>(StringComparer.Ordinal) { item.Id.ToString() };
            var current = item;
            bool isCarried = false;
            while (!string.IsNullOrEmpty(current.ParentId))
            {
                if (current.ParentId == root) { isCarried = true; break; }
                if (!visited.Add(current.ParentId)) throw new InvalidDataException("Scav inventory cycle.");
                if (!byId.TryGetValue(current.ParentId, out current!))
                    throw new InvalidDataException("Scav inventory orphan.");
            }
            if (isCarried) carried.Add(item);
        }
        return carried;
    }

    public static HashSet<string> RetainedIds(IEnumerable<Item> items, IEnumerable<Item> nativeLostRoots)
    {
        var all = items.ToList();
        var lost = nativeLostRoots.Select(x => x.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = false;
            foreach (var item in all)
                if (item.ParentId != null && lost.Contains(item.ParentId)) changed |= lost.Add(item.Id.ToString());
        } while (changed);
        return all.Select(x => x.Id.ToString()).Where(x => !lost.Contains(x)).ToHashSet(StringComparer.Ordinal);
    }

    public static List<Item> LootItems(List<Item> carried, string equipment)
        => carried.Where(x => !(x.ParentId == equipment && x.SlotId == "Pockets")).ToList();

    public static void ApplyRunThrough(List<Item> items, ExitStatus? result)
    {
        if (result != ExitStatus.RUNNER) return;
        foreach (var item in items)
        {
            item.Upd ??= new();
            item.Upd.SpawnedInSession = false;
        }
    }

    public static List<Item> LostInsurance(IEnumerable<Item>? finalLost, HashSet<string> originallyInsured,
        HashSet<string> recovered, HashSet<string> protectedIds)
    {
        var result = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in finalLost ?? [])
        {
            string id = item.Id.ToString();
            if (!seen.Add(id)) throw new InvalidDataException("Duplicate lost insured item.");
            if (!originallyInsured.Contains(id)) throw new InvalidDataException("Unregistered insured item.");
            if (!recovered.Contains(id) && !protectedIds.Contains(id)) result.Add(item);
        }
        // Native insurance normalizes orphaned parents later; do not retain a recovered
        // attachment merely because its original parent is still lost.
        return result;
    }

    /// <summary>Input must already be deep-cloned. Returns one normalized mail graph with new IDs.</summary>
    public static List<Item> MailItems(List<Item> carried, MongoId stash)
    {
        var idMap = carried.ToDictionary(x => x.Id.ToString(), _ => new MongoId(), StringComparer.Ordinal);
        foreach (var item in carried)
        {
            string oldId = item.Id.ToString();
            if (item.ParentId != null && idMap.TryGetValue(item.ParentId, out var parent))
                item.ParentId = parent.ToString();
            else
            {
                item.ParentId = stash.ToString();
                item.SlotId = "main";
                item.Location = null;
            }
            item.Id = idMap[oldId];
        }
        return carried;
    }
}
