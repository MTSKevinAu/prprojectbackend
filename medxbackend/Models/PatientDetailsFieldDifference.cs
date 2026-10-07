namespace medxbackend.Models;

/// <summary>比较同一每日编号下两种输入类型的患者资料。</summary>
public sealed record PatientDetailsFieldDifference(
    string DailyNo,
    int InputTypeA,
    int InputTypeB,
    string Status,
    bool AllFieldsEqual,
    string[] DifferingFields,
    string? FormTypeA,
    string? FormTypeB);
