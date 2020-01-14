﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sirenix.OdinInspector;

public class RegenPassiveEffect : PassiveEffect, ITurnStartPassiveEffect
{
    [HideIf("IsPercentageValue")] public int FlatValue = 0;
    [ShowIf("IsPercentageValue"), PropertyRange(0,1f)] public float PercentageValue = 0f;

    public bool IsPercentageValue = false;

    public async Task OnTurnStart(IBattler battler)
    {
        int healAmount;

        if (IsPercentageValue)
        {
            healAmount = FlatValue;
        }
        else
        {
            healAmount = (int) (battler.Stats.MaxHp * PercentageValue);
        }

        var effect = new HealEffect
        {
            HealAmount = healAmount
        };

        await battler.ReceiveEffect(battler, null, effect);
    }
}

