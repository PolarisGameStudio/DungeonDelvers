﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using SkredUtils;
using UnityEngine;
using UnityEngine.UI;
using Random = System.Random;
// ReSharper disable RedundantAssignment

public class MonsterBattler : AsyncMonoBehaviour, IBattler
{
    [ReadOnly] public Encounter Encounter;
    [OnValueChanged("LoadBase")] public Monster MonsterBase;
    [SerializeField, ReadOnly] private GameObject monsterBattler;
    [SerializeField, ReadOnly] private Image image;
    private BattleController BattleController;
    
    #region Control
    public Dictionary<object, object> BattleDictionary { get; private set; }
    
    private void Awake()
    {
        LoadBase();
    }

    private void Start()
    {
        BattleController = BattleController.Instance;
    }

    public void LoadBase()
    {
        if (MonsterBase == null)
            return;

        var level = UnityEngine.Random.Range(MonsterBase.BaseLevel - MonsterBase.LevelVariance,
            MonsterBase.BaseLevel + MonsterBase.LevelVariance + 1);

        Level = level;
        stats = MonsterBase.Stats + MonsterBase.StatLevelVariance*(Level-MonsterBase.BaseLevel);

        Skills = MonsterBase.Skills;
        MonsterAi = MonsterBase.MonsterAi;

        if (monsterBattler == null)
            monsterBattler = Instantiate(MonsterBase.MonsterBattler, RectTransform);
        if (image == null)
            image = monsterBattler.GetComponent<Image>();

        CurrentHp = Stats.MaxHp;
        CurrentEp = Stats.InitialEp;
        
        BattleDictionary = new Dictionary<object, object>();
        Passives = MonsterBase.Passives;
        StatusEffects = new List<StatusEffect>();
        
        Debug.Log($"Inicializado Lv.{level}{Name}");
    }

    private bool NoMonster => MonsterBase == null;

    #endregion
    
    #region Stats

    [ShowInInspector] public int Level { get; private set; }
    public string Name => MonsterBase.MonsterName;
    [FoldoutGroup("Stats"), ShowInInspector, PropertyOrder(999)] private Stats stats;
    public Stats Stats => stats;
    
    [FoldoutGroup("Stats"), SerializeField] private int currentHp;
    public int CurrentHp
    {
        get => currentHp;
        set
        {
            currentHp = value;
            currentHp = Mathf.Clamp(currentHp, 0, Stats.MaxHp);
        }
    }
    [FoldoutGroup("Stats"), SerializeField] private int currentEp;
    public int CurrentEp
    {
        get => currentEp;
        set
        {
            currentEp = value;
            Mathf.Clamp(currentEp, 0, 100);
        }
    }
    
    public List<MonsterSkill> Skills;
    public List<Passive> Passives { get; private set; }
    [ShowInInspector, ReadOnly] public List<StatusEffect> StatusEffects
    {
        get;
        set;
    }
    public MonsterAI MonsterAi;

    public bool Fainted => CurrentHp == 0;

    //[FoldoutGroup("Passives"), ShowInInspector, Sirenix.OdinInspector.ReadOnly] public List<BattlePassive> Passives { get; set; } = new List<BattlePassive>();

    #endregion
    
    #region TurnEvents

    public async Task TurnStart()
    {
        currentEp += Stats.EpGain;
        Debug.Log($"Começou o turno de {Name}");
        
        var expiredStatusEffects = StatusEffects.Where(statusEffect => statusEffect.TurnDuration <= BattleController.Instance.CurrentTurn).ToArray();

        expiredStatusEffects.ForEach(expired => StatusEffects.Remove(expired));
        
        var turnStartPassives =
            Passives.SelectMany(passive => passive.Effects.Where(effect => effect is ITurnStartPassiveEffect))
                .Concat(StatusEffects.SelectMany(statusEffect => statusEffect.Effects.Where(effect => effect is ITurnStartPassiveEffect))
                    )
                .OrderByDescending(effect => effect.Priority).Cast<ITurnStartPassiveEffect>().ToArray();
        
        if (turnStartPassives.Any())
            await QueueActionAndAwait(() => BattleController.Instance.battleCanvas.BindActionArrow(RectTransform));
        
        foreach (var turnStartPassive in turnStartPassives)
        {
            await turnStartPassive.OnTurnStart(this);
            if (Fainted)
                break;
        }
        
        await QueueActionAndAwait(() => BattleController.Instance.battleCanvas.UnbindActionArrow());
    }

    public async Task TurnEnd()
    {
        Debug.Log($"Acabou o turno de {Name}");
    }
    
    public async Task<Turn> GetTurn()
    {
        if (Fainted || MonsterAi == null)
            return null;
        
        Debug.Log($"Começou a pegar o turno de {Name}");

        Turn turn = new Turn();

        await QueueActionAndAwait(() =>
        {
            turn = MonsterAi.BuildTurn(this);
        });
        
        return turn;
    }

    public async Task ExecuteTurn(IBattler source, Skill skill, IEnumerable<IBattler> targets)
    {
        if (skill.EpCost > CurrentEp)
        {
            Debug.LogError($"{MonsterBase.MonsterName} tentou usar uma skill com custo maior que o EP Atual");
            return;
        }

        CurrentEp -= skill.EpCost;

        if (Skills != null)
        {
            QueueAction(() =>
            {
                BattleController.Instance.battleCanvas.BindActionArrow(RectTransform);
                foreach (var target in targets)
                {
                    BattleController.Instance.battleCanvas.BindTargetArrow(target.RectTransform);
                }
            });
            
            await BattleController.Instance.battleCanvas.battleInfoPanel.DisplayInfo(skill.SkillName);
            
            QueueAction(() =>
            {
                BattleController.Instance.battleCanvas.UnbindActionArrow();
                BattleController.Instance.battleCanvas.CleanTargetArrows();
            });
            
        }
    }
    
    public async Task<IEnumerable<EffectResult>> ReceiveSkill(IBattler source, Skill skill)
    {
        Debug.Log($"Recebendo skill em {Name}");
        
        bool hasHit;

        if (skill.TrueHit)
        {
            Debug.Log($"{source.Name} acertou {Name} com {skill.SkillName} por ter True Hit");
            hasHit = true;
        }
        else
        {
            var accuracy = source.Stats.Accuracy + skill.AccuracyModifier;
            var evasion = Stats.Evasion;

            var hitChance = accuracy - evasion;

            var rng = GameController.Instance.Random.NextDouble();

            hasHit = rng <= hitChance;
            
            Debug.Log($"{source.Name} {(hasHit ? "acertou":"errou")} {Name} com {skill.SkillName} -- Acc: {accuracy:F3}, Eva: {evasion:F3}, Rng: {rng:F3}");
        }
        if (hasHit)
        {
            var results = new List<EffectResult>();

            bool hasCrit;

            if (skill.CanCritical)
            {
                hasCrit = false;
                Debug.Log($"{source.Name} não critou {Name} com {skill.SkillName} por não poder critar.");
            }
            else
            {
                var critAccuracy = source.Stats.CritChance + skill.CriticalModifier;
                var critEvasion = Stats.CritAvoid;

                var critChance = critAccuracy - critEvasion;

                var rng = GameController.Instance.Random.NextDouble();

                hasCrit = rng <= critChance;
                
                Debug.Log($"{source.Name} {(hasCrit ? "":"não")} critou {Name} com {skill.SkillName} -- Acc: {critAccuracy}, Eva: {critEvasion}, Rng: {rng}");
            }
            
            var skillInfo = new SkillInfo
            {
                HasCrit = hasCrit,
                Skill = skill,
                Source = source,
                Target = this
            };

            var effects = hasCrit ? skill.CriticalEffects : skill.Effects;
            
            foreach (var effect in effects)
            {
                results.Add(
                    await ReceiveEffect(new EffectInfo
                    {
                        SkillInfo = skillInfo,
                        Effect = effect
                    }));
            }
            return results;
        }
        else
        {
            await BattleController.Instance.battleCanvas.ShowSkillResult(this, "Miss!", Color.white);
            return new EffectResult[] { };
            //retonar missresult depois(?)
        }
    }

    public async Task<EffectResult> ReceiveEffect(EffectInfo effectInfo)
    {
        EffectResult effectResult = null;
        
        await QueueActionAndAwait(() =>
        {
            effectResult = effectInfo.Effect.ExecuteEffect(effectInfo.SkillInfo);
        });

        //Ver pra mostrar Miss! quando o golpe errar, mostrar vermelho quando crita

        switch (effectResult)
        {
            case DamageEffect.DamageEffectResult damageEffectResult when !Fainted:
            {
                // Task damage = ShowDamage(damageEffectResult.DamageDealt);
                // Task flash = DamageFlash();
                //
                // await Task.WhenAll(flash, damage);

                await ShowDamageAndFlash(damageEffectResult.DamageDealt);
                
                break;
            }
            case DamageEffect.DamageEffectResult damageEffectResult:
            {
                Task damage = ShowDamage(damageEffectResult.DamageDealt);
                Task fade = Fade();
                
                await Task.WhenAll(fade, damage);
                break;
            }
            case HealEffect.HealEffectResult healEffectResult:
            {
                await BattleController.Instance.battleCanvas.ShowSkillResult(this, healEffectResult.AmountHealed.ToString(),
                    Color.green);
                break;
            } 
        }

        return effectResult;
    }

    public async Task AfterSkill(IEnumerable<EffectResult> result)
    {
        //Processar o que aconteceu quando usou a skill
    }

    #endregion

    #region Animations

    private async Task ShowDamageAndFlash(int damageAmount)
    {
        await PlayCoroutine(ShowDamageAndFlashCoroutine(damageAmount));
    }

    private IEnumerator ShowDamageAndFlashCoroutine(int damageAmount)
    {
        var damageInstance = Instantiate(BattleController.Instance.battleCanvas.DamagePrefab, BattleController.Instance.battleCanvas.transform);
        damageInstance.transform.position = transform.position + new Vector3(0, 100, 0);

        var damageComponent = damageInstance.GetComponent<DamageText>();
        damageComponent.SetupDamageText(damageAmount.ToString(), Color.white);
        
        var normalColor = image.color;
        var blinkingColor = new Color(image.color.r, image.color.g, image.color.b, 0.3f);
        
        for (int i = 0; i < 5; i++)
        {
            image.color = blinkingColor;
            yield return new WaitForSeconds(0.05f);
            image.color = normalColor;
            yield return new WaitForSeconds(0.05f);
        }
        
        yield return new WaitForSeconds(1);
        
        Destroy(damageInstance);
    }
    
    private async Task ShowDamage(int damageAmount)
    {
        await PlayCoroutine(ShowDamageCoroutine(damageAmount), this);
    }

    private IEnumerator ShowDamageCoroutine(int damageAmount)
    {
        var damageInstance = Instantiate(BattleController.Instance.battleCanvas.DamagePrefab, BattleController.Instance.battleCanvas.transform);
        damageInstance.transform.position = transform.position + new Vector3(0, 100, 0);

        var damageComponent = damageInstance.GetComponent<DamageText>();
        damageComponent.SetupDamageText(damageAmount.ToString(), Color.white);
        
        yield return new WaitForSeconds(1.4f);
        
        Destroy(damageInstance);
    }

    private async Task ShowHeal()
    {
        
    }
    
    private async Task DamageFlash()
    {
        await PlayCoroutine(DamageBlinkCoroutine());
    }

    private IEnumerator DamageBlinkCoroutine()
    {
        var normalColor = image.color;
        var blinkingColor = new Color(image.color.r, image.color.g, image.color.b, 0.3f);

        for (int i = 0; i < 5; i++)
        {
            image.color = blinkingColor;
            yield return new WaitForSeconds(0.05f);
            image.color = normalColor;
            yield return new WaitForSeconds(0.05f);
        }
    }

    private async Task Fade()
    {
        await PlayCoroutine(FadeCoroutine());
    }
    
    private IEnumerator FadeCoroutine(float speed = 0.05f)
    {
        while (image.color.a > 0)
        {
            image.color = new Color(image.color.r, image.color.g, image.color.b, image.color.a - speed);
            yield return new WaitForFixedUpdate();
        }
    }

    #endregion
   
    
    public RectTransform RectTransform => transform as RectTransform;
}