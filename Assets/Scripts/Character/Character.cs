using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;


public class Character
{
    public CharacterBase Base { get; private set; }

    #region Instancing

    public CharacterSave Serialize()
    {
        var save = new CharacterSave()
        {
            baseUid = GameSettings.Instance.CharacterDatabase.GetId(Base).Value,
            currentHp = currentHp,
            serializedMasteryInstances = MasteryInstances.Select(mI => mI.Serialize()).ToArray(),
            Equipment = EquippableSaves(),
            masteryPoints = CurrentMp,
        };

        return save;
    }

    public Character(CharacterBase characterBase)
    {
        Base = characterBase;
        Weapon = ItemInstanceBuilder.BuildInstance(Base.Weapon) as Equippable;
        Head = ItemInstanceBuilder.BuildInstance(Base.Head) as Equippable;
        Body = ItemInstanceBuilder.BuildInstance(Base.Body) as Equippable;
        Hand = ItemInstanceBuilder.BuildInstance(Base.Hand) as Equippable;
        Feet = ItemInstanceBuilder.BuildInstance(Base.Feet) as Equippable;
        Accessory = ItemInstanceBuilder.BuildInstance(Base.Accessory) as Equippable;
        MasteryInstances = Base.Masteries.Initialize(this);

        Regenerate();

        CurrentHp = Stats.MaxHp;
    }

    public Character(CharacterSave save)
    {
        try
        {
            //Base = GameDatabase.Database.CharacterBases.Find(x => x.uniqueIdentifier == save.baseUid);
            var baseExists = GameSettings.Instance.CharacterDatabase.Content.TryGetValue(save.baseUid, out var characterBase);
            if (!baseExists)
            {
                Debug.LogError("Missing character base");
                throw new Exception();
            }

            Base = characterBase;
            MasteryInstances = Base.Masteries.Initialize(this, save.serializedMasteryInstances);

            Weapon = ItemInstanceBuilder.BuildInstance(save.Equipment[0]) as Equippable;
            Head = ItemInstanceBuilder.BuildInstance(save.Equipment[1]) as Equippable;
            Body = ItemInstanceBuilder.BuildInstance(save.Equipment[2]) as Equippable;
            Hand = ItemInstanceBuilder.BuildInstance(save.Equipment[3]) as Equippable;
            Feet = ItemInstanceBuilder.BuildInstance(save.Equipment[4]) as Equippable;
            Accessory = ItemInstanceBuilder.BuildInstance(save.Equipment[5]) as Equippable;

            Regenerate();

            currentHp = save.currentHp;
            CurrentMp = save.masteryPoints;
        }
        catch (Exception e)
        {
            throw new DeserializationFailureException(typeof(Character));
        }
    }

    #endregion

    #region Stats

    private int CurrentLevel => PlayerController.Instance.PartyLevel;

    public int CurrentMp;

    [FoldoutGroup("Stats"), ShowInInspector, Sirenix.OdinInspector.ReadOnly]
    private int currentHp;

    public int CurrentHp
    {
        get => currentHp;
        set
        {
            currentHp = value;
            currentHp = Mathf.Clamp(currentHp, 0, Stats.MaxHp);
        }
    }

    public bool Fainted => CurrentHp == 0;

    [FoldoutGroup("Stats"), Sirenix.OdinInspector.ReadOnly]
    public Stats BaseStats;

    [FoldoutGroup("Stats"), Sirenix.OdinInspector.ReadOnly]
    public Stats BonusStats;

    [FoldoutGroup("Stats"), Sirenix.OdinInspector.ReadOnly]
    public Stats Stats;

    [FoldoutGroup("Equips")] public Equippable Weapon;
    [FoldoutGroup("Equips")] public Equippable Head;
    [FoldoutGroup("Equips")] public Equippable Body;
    [FoldoutGroup("Equips")] public Equippable Hand;
    [FoldoutGroup("Equips")] public Equippable Feet;
    [FoldoutGroup("Equips")] public Equippable Accessory;

    [ShowInInspector] public List<PlayerSkill> Skills { get; private set; }

    [ShowInInspector] public List<Passive> Passives { get; private set; }

    [ShowInInspector] public List<MasteryInstance> MasteryInstances { get; private set; }
    
    public IEnumerable<Equippable> Equipment
    {
        get
        {
            return new Equippable[] {Weapon, Head, Body, Hand, Feet, Accessory}.Where(equippable =>
                equippable != null);
        }
    }

    private EquippableSave[] EquippableSaves()
    {
        return new Equippable[] {Weapon, Head, Body, Hand, Feet, Accessory}.Select(equipment =>
        {
            if (equipment == null)
                return new EquippableSave
                {
                    baseUid = -1
                };

            return equipment.Serialize();
        }).Cast<EquippableSave>().ToArray();
    }

    #endregion

    #region Updating

    public void LevelUp()
    {
        CurrentMp += (CurrentLevel / 5) + 1;
        Regenerate();
    }

    public void Regenerate()
    {
        //Fazer validações em tudo, eg. ver se tudo que tá equipado é valido, masteries tem nivel valido (>= 0, <= max)
        var stopwatch = Stopwatch.StartNew();

        RecalculateBases();
        RecalculateBonus();
        LoadSkills();
        LoadPassives();
        LoadMasteries();

        Stats = BaseStats + BonusStats;

        stopwatch.Stop();
        Debug.Log($"{Base.CharacterName} regenerado em {stopwatch.ElapsedMilliseconds}ms");
    }

    private void RecalculateBases()
    {
        BaseStats = Base.Bases + (Base.Growths * CurrentLevel);
    }

    private void RecalculateBonus()
    {
        BonusStats = new Stats();

        foreach (var equipInstance in Equipment)
        {
            var equip = equipInstance.EquippableBase;
            BonusStats += equip.BaseStats;
        }
    }

    private void LoadSkills()
    {
        Skills = new List<PlayerSkill>();
        Skills.AddRange(Base.BaseSkills);

        foreach (var equippable in Equipment)
        {
            Skills.AddRange(equippable.EquippableBase.Skills);
        }
    }

    private void LoadPassives()
    {
        Passives = new List<Passive>();
        Passives.AddRange(Base.BasePassives);

        foreach (var equippable in Equipment)
        {
            Passives.AddRange(equippable.EquippableBase.Passives);
        }
    }

    private void LoadMasteries()
    {
        foreach (var masteryInstance in MasteryInstances)
        {
            masteryInstance.ApplyEffects();
        }
    }

    public void Equip(Equippable equippable)
    {
        if (equippable == null || equippable.EquippableBase == null)
            throw new NullReferenceException();

        var slot = equippable.EquippableBase.Slot;

        if (!CanEquip(equippable.EquippableBase))
        {
            Debug.LogError($"{Base.CharacterName} não pode equipar {equippable.EquippableBase.itemName}");
            return;
        }

        switch (slot)
        {
            case EquippableBase.EquippableSlot.Accessory:
            {
                var old = Unequip(EquippableBase.EquippableSlot.Accessory);
                Accessory = equippable;
                OnEquip.Invoke(Accessory);
                break;
            }
            case EquippableBase.EquippableSlot.Body:
            {
                Unequip(EquippableBase.EquippableSlot.Body);
                Body = equippable;
                OnEquip.Invoke(Body);
                break;
            }
            case EquippableBase.EquippableSlot.Feet:
            {
                Unequip(EquippableBase.EquippableSlot.Feet);
                Feet = equippable;
                OnEquip.Invoke(Feet);
                break;
            }
            case EquippableBase.EquippableSlot.Hand:
            {
                Unequip(EquippableBase.EquippableSlot.Hand);
                Hand = equippable;
                OnEquip.Invoke(Hand);
                break;
            }
            case EquippableBase.EquippableSlot.Head:
            {
                Unequip(EquippableBase.EquippableSlot.Head);
                Head = equippable;
                OnEquip.Invoke(Head);
                break;
            }
            case EquippableBase.EquippableSlot.Weapon:
            {
                Unequip(EquippableBase.EquippableSlot.Weapon);
                Weapon = equippable;
                OnEquip.Invoke(Weapon);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        PlayerController.Instance.Inventory.Remove(equippable);

        Regenerate();
    }

    public Equippable Unequip(EquippableBase.EquippableSlot slot)
    {
        Equippable old;

        switch (slot)
        {
            case EquippableBase.EquippableSlot.Weapon:
                old = Weapon;
                Weapon = null;
                break;
            case EquippableBase.EquippableSlot.Head:
                old = Head;
                Head = null;
                break;
            case EquippableBase.EquippableSlot.Body:
                old = Body;
                Body = null;
                break;
            case EquippableBase.EquippableSlot.Hand:
                old = Hand;
                Hand = null;
                break;
            case EquippableBase.EquippableSlot.Feet:
                old = Feet;
                Feet = null;
                break;
            case EquippableBase.EquippableSlot.Accessory:
                old = Accessory;
                Accessory = null;
                break;
            default:
                throw new ArgumentException();
        }

        if (old != null)
        {
            PlayerController.Instance.Inventory.Add(old);
            OnUnequip.Invoke(old);
            Regenerate();
        }

        return old;
    }

    public bool CanEquip(EquippableBase equippable)
    {
        if (equippable is WeaponBase weaponBase)
        {
            return Base.WeaponTypes.Contains(weaponBase.weaponType);
        }
        else if (equippable is IArmorTypeEquipment armorBase)
        {
            return Base.ArmorTypes.Contains(armorBase.ArmorType);
        }
        else
            return true;
    }

    public bool CanEquip(Equippable equippable) => CanEquip(equippable.EquippableBase);

    #endregion

    #region Events

    public EquipEvent OnEquip = new EquipEvent();
    public EquipEvent OnUnequip = new EquipEvent();

    #endregion

#if UNITY_EDITOR
    [FoldoutGroup("Equips"), PropertyOrder(-1), ShowInInspector, OnValueChanged("_EquipBase")]
    private EquippableBase _ToEquip;

    private void _EquipBase()
    {
        if (_ToEquip == null)
            return;
        var equip = ItemInstanceBuilder.BuildInstance(_ToEquip) as Equippable;
        Equip(equip);
        _ToEquip = null;
    }
#endif

    public Equippable GetSlot(EquippableBase.EquippableSlot slot)
    {
        switch (slot)
        {
            case EquippableBase.EquippableSlot.Weapon:
                return Weapon;
            case EquippableBase.EquippableSlot.Hand:
                return Hand;
            case EquippableBase.EquippableSlot.Head:
                return Head;
            case EquippableBase.EquippableSlot.Body:
                return Body;
            case EquippableBase.EquippableSlot.Feet:
                return Feet;
            case EquippableBase.EquippableSlot.Accessory:
                return Accessory;
            default:
                throw new ArgumentException();
        }
    }
}

[Serializable] public class EquipEvent : UnityEvent<Equippable>
{
    public EquipEvent()
    {
    }
}

[Serializable] public class CharacterEvent : UnityEvent<Character>
{
}