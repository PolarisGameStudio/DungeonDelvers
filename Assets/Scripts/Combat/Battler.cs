﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using Sirenix.Utilities;
using SkredUtils;
using UnityEngine;
using UnityEngine.UI;

public abstract class Battler : AsyncMonoBehaviour
{
    public Dictionary<object, object> BattleDictionary { get; set; } = new Dictionary<object, object>();

    #region UnityEvents

    protected virtual void Awake()
    {
        this.Ensure(ref AudioSource);
        AudioSource.outputAudioMixerGroup = GameSettings.Instance.SFXChannel;
    }

    #endregion
    
    #region Fields

    public virtual string BattlerName { get; protected set; }
    public virtual int Level { get; protected set; }
    [FoldoutGroup("Stats")] public abstract int CurrentHp { get; set; }
    [FoldoutGroup("Stats")] public abstract int CurrentAp { get; set; }
    public bool Fainted => CurrentHp == 0;
    public SoundInfo HitSound;
    public abstract IEnumerable<Skill> SkillList { get; }
    
    #endregion

    #region Stats

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] public Stats Stats { get; set; }
    
    [TabGroup("Passives"), ShowInInspector, Sirenix.OdinInspector.ReadOnly] public virtual List<Passive> Passives { get; protected set; }
    [TabGroup("Status Effects"), ShowInInspector] public virtual List<StatusEffectInstance> StatusEffectInstances { get; set; }

    public virtual PassiveEffect[] PassiveEffects
    {
        get
        {
            var passiveEffectsFromPassives = Passives.SelectMany(pE => pE.Effects);
            var passiveEffectsFromStatusEffects = StatusEffectInstances
                .SelectMany(sI => sI.StatusEffect.Effects);
            return passiveEffectsFromPassives.Concat(passiveEffectsFromStatusEffects).ToArray();
        }
    }

    public PassiveEffect[] OrderedPassiveEffects => 
        PassiveEffects.OrderByDescending(pE => pE.Priority).ToArray();

    public virtual RectTransform RectTransform => transform as RectTransform;
    public RectTransform StatusEffectRect;
    
    protected AudioSource AudioSource;

    #endregion

    #region BattleEvents

    public async Task TurnStart()
    {
        CurrentAp += GameSettings.Instance.ApGain;
        Debug.Log($"Começou o turno de {BattlerName}");

        StatusEffectInstances.ForEach(instance => { instance.TurnDuration--; });
        
        var expiredStatusEffects = StatusEffectInstances
            .Where(statusEffect => statusEffect.TurnDuration < 0)
            .ToArray();

        await Task.WhenAll(expiredStatusEffects
            .Select(RemoveStatusEffectAsync));

        var effectsFromPassives = Passives
            .SelectMany(passive => passive.Effects
                .Where(effect => effect is ITurnStartPassiveEffect)
                .Select(effect => (passive as object, effect))).ToArray();

        var effectsFromStatuses = StatusEffectInstances
            .SelectMany(instance => instance.StatusEffect.Effects
                .Where(effect => effect is ITurnStartPassiveEffect)
                .Select(effect => (instance as object, effect))).ToArray();

        var turnStartEffects = effectsFromPassives
            .Concat(effectsFromStatuses)
            .OrderByDescending(turnStartEffect => turnStartEffect.effect.Priority)
            .Select(turnStartEffect => (turnStartEffect.Item1,turnStartEffect.Item2 as ITurnStartPassiveEffect))
            .ToArray();

        foreach (var turnStartPassive in turnStartEffects)
        {
            if (turnStartPassive.Item1 is Passive passive)
            {
                await turnStartPassive.Item2.OnTurnStart(new PassiveEffectInfo
                {
                    PassiveEffectSourceName = passive.PassiveName,
                    Source = this,
                    Target = this
                });
            }
            else if (turnStartPassive.Item1 is StatusEffectInstance statusEffectInstance)
            {
                await turnStartPassive.Item2.OnTurnStart(new PassiveEffectInfo
                {
                    PassiveEffectSourceName = statusEffectInstance.StatusEffect.StatusEffectName,
                    Source = statusEffectInstance.Source,
                    Target = statusEffectInstance.Target
                });
            }
            else
            {
                throw new ArgumentException();
            }
        }
    }

    public async Task TurnEnd()
    {
        Debug.Log($"Acabou o turno de {BattlerName}.");
    }

    public abstract Task<Turn> GetTurn();
    
    public async Task ExecuteTurn(Turn turn)
    {
        Debug.Log($"Executando o turno de {BattlerName}.");

        if (turn == null)
            return;

        if (turn.Skill.ApCost > CurrentAp)
        {
            Debug.LogError(
                $"{BattlerName} tentou usar uma skill com custo maior que o EP Atual. SkillName: {turn.Skill.SkillName}, SkillCost: {turn.Skill.ApCost}, CurrentAp: {CurrentAp}");
        }

        CurrentAp -= turn.Skill.ApCost;

        QueueAction(() => { BattleController.Instance.battleCanvas.battleInfoPanel.ShowInfo(turn.Skill.SkillName); });

        await AnimateTurn(turn);
        
        QueueAction(() => {BattleController.Instance.battleCanvas.battleInfoPanel.HideInfo();});
    }

    public async Task<IEnumerable<EffectResult>> ReceiveSkill(Battler source, Skill skill)
    {
        Debug.Log($"Recebendo skill em {BattlerName}");
        bool hasHit;
        
        if (skill.TrueHit)
        {
            Debug.Log($"{source.BattlerName} acertou {BattlerName} com {skill.SkillName} por ter True Hit");
            hasHit = true;
        }
        else
        {
            var accuracy = source.Stats.Accuracy + skill.AccuracyModifier;
            var evasion = Stats.Evasion;

            var hitChance = accuracy - evasion;

            var rng = GameController.Instance.Random.NextDouble();

            hasHit = rng <= hitChance;
            
            Debug.Log($"{source.BattlerName} {(hasHit ? "acertou":"errou")} {BattlerName} com {skill.SkillName} -- Acc: {accuracy}, Eva: {evasion}, Rng: {rng:F3}");
        }
        if (hasHit)
        {
            var results = new List<EffectResult>();

            bool hasCrit;

            if (!skill.CanCritical)
            {
                hasCrit = false;
                Debug.Log($"{source.BattlerName} não critou {BattlerName} com {skill.SkillName} por não poder critar.");
            }
            else
            {
                var critAccuracy = source.Stats.CritChance + skill.CriticalModifier;
                var critEvasion = Stats.CritAvoid;

                var critChance = critAccuracy - critEvasion;

                var rng = GameController.Instance.Random.NextDouble();

                hasCrit = rng <= critChance;
                
                Debug.Log($"{source.BattlerName} {(hasCrit ? "":"não")} critou {BattlerName} com {skill.SkillName} -- Acc: {critAccuracy}, Eva: {critEvasion}, Rng: {rng:F3}");
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
            await BattleController.Instance.battleCanvas.ShowSkillResultAsync(this, "Miss!", Color.white);
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

        await AnimateEffectResult(effectResult);

        return effectResult;
    }

    public async Task AfterSkill(IEnumerable<EffectResult> results)
    {
    }

    protected abstract Task AnimateTurn(Turn turn);
    protected abstract Task AnimateEffectResult(EffectResult effectResult);

    #endregion

    #region Functions

    public virtual void BuildStatusEffectRect()
    {
        if (StatusEffectRect != null)
            Destroy(StatusEffectRect.gameObject);
        var statusEffectRect = new GameObject($"{BattlerName} StatusEffects");
        var rect = statusEffectRect.AddComponent<RectTransform>();
        rect.SetParent(transform, false);
        rect.localPosition = new Vector3(0,50,0);
        rect.sizeDelta = new Vector2(0,40);
        var hlg = statusEffectRect.AddComponent<HorizontalLayoutGroup>();
        hlg.childControlHeight = false;
        hlg.childControlWidth = false;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth = false;
        hlg.spacing = 5;
        var csf = statusEffectRect.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.MinSize;
        StatusEffectRect = rect;
    }

    public virtual void RebuildStatusEffectIcons()
    {
        foreach (Transform child in StatusEffectRect)
        {
            Destroy(child.gameObject);
        }

        var spritesToAdd = new List<Sprite>();

        foreach (var statusEffectInstance in StatusEffectInstances)
        {
            if (statusEffectInstance.StatusEffect.CustomIcon != null)
            {
                spritesToAdd.Add(statusEffectInstance.StatusEffect.CustomIcon);
            }
            else if (GameSettings.Instance.StatusEffectTypeSprites.TryGetValue(
                statusEffectInstance.StatusEffect.Type,
                out var sprite))
            {
                spritesToAdd.Add(sprite);
            }
        }

        foreach (var sprite in spritesToAdd)
        {
            var gameObject = new GameObject("StatusSprite");
            var rect = gameObject.AddComponent<RectTransform>();
            rect.SetParent(StatusEffectRect, false);
            rect.sizeDelta = new Vector2(40, 40);
            var image = gameObject.AddComponent<Image>();
            image.sprite = sprite;
        }
    }
    
    public void PlaySound(AudioClip clip, float volume)
    {
        AudioSource.PlayOneShot(clip,volume);
    }
    
    protected virtual Task PlayHitSound() => HitSound != null
        ? QueueActionAndAwait(() => AudioSource.PlayOneShot(HitSound))
        : Task.CompletedTask;

    public abstract void RecalculateStats();

    public async Task ApplyStatusEffectAsync(StatusEffectInstance statusEffectInstance)
    {
        await QueueActionAndAwait(() => ApplyStatusEffect(statusEffectInstance));
    }

    public async Task RemoveStatusEffectAsync(StatusEffectInstance statusEffectInstance)
    {
        await QueueActionAndAwait(() => RemoveStatusEffect(statusEffectInstance));
    }

    public void ApplyStatusEffect(StatusEffectInstance statusEffectInstance)
    {
        var statusEffect = statusEffectInstance.StatusEffect;
        var appliedStatusEffect = StatusEffectInstances.FirstOrDefault(sI => sI.StatusEffect == statusEffect);
        if (appliedStatusEffect != null)
        {
            if (statusEffectInstance.TurnDuration > appliedStatusEffect.TurnDuration)
                appliedStatusEffect.TurnDuration = statusEffectInstance.TurnDuration;
            return;
        }

        var cancellable = statusEffect.Cancels;
        var cancelledStatusEffects = StatusEffectInstances
            .Where(sI => cancellable.Contains(sI.StatusEffect)).ToArray();

        foreach (var cancelledStatusEffect in cancelledStatusEffects)
        {
            RemoveStatusEffect(cancelledStatusEffect);
        }

        StatusEffectInstances.Add(statusEffectInstance);
        var effects = statusEffectInstance.StatusEffect.Effects
            .Where(effect => effect is IOnApplyPassiveEffect)
            .Cast<IOnApplyPassiveEffect>();

        foreach (var effect in effects)
        {
            effect.OnApply(this);
        }

        RecalculateStats();

        if (statusEffect.Type != StatusEffect.StatusEffectType.None ||
            statusEffectInstance.StatusEffect.CustomIcon != null)
            RebuildStatusEffectIcons();
    }

    public void RemoveStatusEffect(StatusEffectInstance statusEffectInstance)
    {
        var removed = StatusEffectInstances.Remove(statusEffectInstance);
        
        if (!removed)
            return;

        var effects = statusEffectInstance.StatusEffect.Effects
            .Where(effect => effect is IOnApplyPassiveEffect)
            .Cast<IOnApplyPassiveEffect>();

        foreach (var effect in effects)
        {
            effect.OnUnapply(this);
        }
        
        RecalculateStats();
        
        if (statusEffectInstance.StatusEffect.Type != StatusEffect.StatusEffectType.None || statusEffectInstance.StatusEffect.CustomIcon != null)
            RebuildStatusEffectIcons();
    }

    #endregion
}

#region PassiveInterfaces

public interface ITurnStartPassiveEffect
{
    Task OnTurnStart(PassiveEffectInfo passiveEffectInfo);
}

public interface IStatModifierPassiveEffect
{
    void AddBonuses(Battler battler, ref Stats bonuses);
}

public interface IOnApplyPassiveEffect
{
    void OnApply(Battler battler);
    void OnUnapply(Battler battler);
}

public interface IBuildTurnOverridePassiveEffect
{
    Task<Turn> BuildTurnOverride(Battler battler);
}

#endregion