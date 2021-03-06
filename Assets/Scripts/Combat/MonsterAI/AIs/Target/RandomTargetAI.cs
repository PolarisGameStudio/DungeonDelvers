﻿using System.Linq;
using SkredUtils;

public class RandomTargetAI : ITargetSelector
{
    public Battler[] GetTargets(Battler source, Skill skill)
    {
        var possibleTargets = BattleController.Instance.BuildPossibleTargets(source, skill.Target);
        return possibleTargets.WeightedRandom(battlers =>
        {
            return battlers.Select(battler =>
            {
                if (battler is CharacterBattler characterBattler) return characterBattler.Aggro;
                return 100;
            }).Sum();
        });
    }
}
