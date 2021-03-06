﻿using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleInfoPanel : SerializedMonoBehaviour
{
    public GameObject Panel;
    public TMP_Text Text;

    public async Task DisplayInfo(string info, int duration = 2000)
    {
        GameController.Instance.QueueAction(() => ShowInfo(info));
        await Task.Delay(duration);
        GameController.Instance.QueueAction(() => HideInfo());
        
    }

    public void ShowInfo(string info)
    {
        Panel.SetActive(true);
        Text.text = info;
        LayoutRebuilder.ForceRebuildLayoutImmediate(Panel.transform as RectTransform);
    }

    public void HideInfo()
    {
        Panel.SetActive(false);
    }
}
