using System;
using System.Collections.Generic;
using System.Linq;
using BunnyLoader.Interop;

namespace Terraria.ModLoader;

public abstract class AbstractNPCShop
{
    public class Entry
    {
        readonly GameHandle pin;
        readonly List<Condition> conditions;
        public Item Item { get; }
        public IEnumerable<Condition> Conditions => conditions;
        public bool Disabled { get; private set; }
        public Entry(Item item, params Condition[] conditions)
        {
            if (item.IsNull) throw new ArgumentException("Shop item cannot be null", nameof(item));
            Item = item;
            pin = new GameHandle(item.Pointer);
            this.conditions = new(conditions);
        }
        public void Disable() => Disabled = true;
        public void AddCondition(Condition condition) { ArgumentNullException.ThrowIfNull(condition); conditions.Add(condition); }
        public bool ConditionsMet() { foreach (Condition condition in conditions) if (!condition.IsMet()) return false; return true; }
    }

    internal int Index = -1;
    public int NpcType { get; }
    public string Name { get; }
    public string FullName => NPCShopDatabase.GetShopName(NpcType, Name);
    protected abstract IEnumerable<Entry> AllEntries { get; }
    public IEnumerable<Entry> ActiveEntries => AllEntries.Where(e => !e.Disabled);
    public AbstractNPCShop(int npcType, string name = "Shop")
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        NpcType = npcType;
        Name = name;
    }
    public void Register() => NPCShopDatabase.AddShop(this);
    /**
     * Registro automatico: o AddShops de alguns mods (Fargo) cria as lojas com
     * `new NPCShop(tipo, nome)` e so chama Register() no fim; nesse binario a
     * cauda do metodo nao roda, entao nada era registrado e a loja abria vazia.
     * Enquanto o AddShops de um NPC de mod esta rodando, construir a loja ja a
     * registra — as entradas continuam sendo adicionadas no mesmo objeto.
     */
    protected void AutoRegister() { if (NPCShopDatabase.RegisteringShops) Register(); }
    public abstract void FillShop(ICollection<Item> items, NPC npc);
    public abstract void FillShop(Item[] items, NPC npc, out bool overflow);
    public virtual void FinishSetup() { }
    public virtual void RefreshItems(bool onlyIfVariantChanged = true) { foreach (Entry entry in AllEntries) entry.Item.Refresh(onlyIfVariantChanged); }
}
