namespace QuantTrading.Api.Services.AI;

public interface ISkillPromptModule
{
    string SkillId { get; }
    string BuildPrompt(AiChatInput input);
}

public interface ISkillPromptComposer
{
    string Compose(string? skillId, AiChatInput input);
}

public sealed class SkillPromptComposer : ISkillPromptComposer
{
    private readonly IReadOnlyDictionary<string, ISkillPromptModule> _modules;

    public SkillPromptComposer(IEnumerable<ISkillPromptModule> modules)
    {
        _modules = modules
            .Where(item => !string.IsNullOrWhiteSpace(item.SkillId))
            .ToDictionary(item => item.SkillId, item => item, StringComparer.OrdinalIgnoreCase);
    }

    public string Compose(string? skillId, AiChatInput input)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return string.Empty;
        }

        return _modules.TryGetValue(skillId.Trim(), out var module)
            ? module.BuildPrompt(input)
            : string.Empty;
    }
}

public abstract class BaseSkillPromptModule : ISkillPromptModule
{
    public abstract string SkillId { get; }
    protected abstract string PromptText { get; }

    public virtual string BuildPrompt(AiChatInput input)
    {
        _ = input;
        return PromptText;
    }
}

public sealed class CrossMarketSelectionSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "cross-market-selection";
    protected override string PromptText => "Skill: 跨市场选股。优先按估值、趋势和流动性筛选，结果请给出可执行的候选清单。";
}

public sealed class TechnicalDiagnosisSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "technical-diagnosis";
    protected override string PromptText => "Skill: 技术面诊断。请从趋势、结构、量价、关键位与失效条件给出结论。";
}

public sealed class FinancialResearchSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "financial-research";
    protected override string PromptText => "Skill: 财报研究。请拆解收入、利润、现金流和估值变化，标注核心变量。";
}

public sealed class SmartMoneyTrackingSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "smart-money-tracking";
    protected override string PromptText => "Skill: 聪明钱追踪。重点分析成交异动、资金流向与板块联动。";
}

public sealed class AdvancedOrderSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "advanced-order";
    protected override string PromptText => "Skill: 进阶下单。输出分批入场、止损、止盈与仓位控制建议。";
}

public sealed class PositionReviewSkillPromptModule : BaseSkillPromptModule
{
    public override string SkillId => "position-review";
    protected override string PromptText => "Skill: 持仓复盘。总结得失、风险暴露和下一步调整动作。";
}
