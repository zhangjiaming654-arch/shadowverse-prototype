namespace Shadowverse.Engine.Game;

public enum HandPlayForm { Normal, Enhance, Accelerate, Crystallize }

/// <summary>The current cost/form and per-card Oath gauge, calculated by the rules engine.</summary>
public sealed record HandCardReadout(int Cost, HandPlayForm Form, int? OathGauge, int? OathThreshold, int? SuperOathThreshold)
{
    public string FormName => Form switch
    {
        HandPlayForm.Enhance => "爆能强化", HandPlayForm.Accelerate => "激奏",
        HandPlayForm.Crystallize => "结晶", _ => "本体"
    };
    public bool OathReady => OathGauge >= OathThreshold && OathThreshold is not null;
    public bool SuperOathReady => OathGauge >= SuperOathThreshold && SuperOathThreshold is not null;
    public string GaugeText => OathGauge is not int gauge ? "" :
        SuperOathThreshold is int super && (OathThreshold is null || OathReady)
        ? $"解放 {gauge}/{super}" : $"奥义 {gauge}/{OathThreshold}";
    public int GaugeTarget => (SuperOathThreshold is int super && (OathThreshold is null || OathReady))
        ? super : OathThreshold ?? 1;
    public string Description => $"当前费用：{Cost} PP · {FormName}" +
        (OathGauge is int gauge ? $"\n奥义槽：{gauge} = 当前回合数 + 本卡在手牌中时己方随从进化次数" +
            (OathThreshold is int oath ? $"\n奥义 {gauge}/{oath} · {(OathReady ? "已就绪" : "未就绪")}" : "") +
            (SuperOathThreshold is int super ? $"\n解放奥义 {gauge}/{super} · {(SuperOathReady ? "已就绪" : "未就绪")}" : "") : "");
}
