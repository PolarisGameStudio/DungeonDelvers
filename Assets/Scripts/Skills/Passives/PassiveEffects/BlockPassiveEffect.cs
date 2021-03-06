﻿using UnityEngine;

public class BlockPassiveEffect : PassiveEffect, DamageEffect.IReceiveDamageOverride
{
    public DamageType? OverrideType;
    [Range(0f,1f)] public float OverrideChance;
    
    public EffectResult OverrideReceiveDamage(SkillInfo skillInfo, DamageEffect damageEffect)
    {
        if (OverrideType.HasValue && OverrideType.Value != damageEffect.DamageType)
            return null;

        var chance = Random.Range(0f,1f);
        return chance <= OverrideChance ? new BlockedResult() : null;
    }

    public class BlockedResult : EffectResult
    {
        //precisa de mais info?
    }
}
