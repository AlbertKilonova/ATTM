namespace ATTM.Core.Syntax;

/// <summary>诊断码。数值稳定，便于测试与文档引用。</summary>
public enum DiagnosticCode
{
    // 结构 / 指令
    MissingBpm = 1,
    MissingKeySignature = 2,
    DirectiveSyntax = 3,
    DuplicateDirective = 4,
    DirectiveAfterContent = 5,

    // 词法 / 音符
    InvalidDigit = 101,
    InvalidCharacter = 102,
    SharpWithoutDigit = 103,
    UnclosedParenthesis = 104,
    EmptyChord = 105,
    RestInChord = 106,
    UnitCountNotPowerOfTwo = 107,
    InvalidSegmentCount = 108,
    VelocityOutOfRange = 109,
    InvalidKeyRoot = 110,
    InvalidTimeSignature = 111,
    BpmOutOfRange = 112,
    UnitTooShort = 113,
    EmptySegment = 114,
    PitchOutOfRange = 115,
    EmptyMeasure = 116,
    OctaveMarkerWithoutDigit = 117,

    // 行八度前缀 / 连奏
    LineOctaveNotAtStart = 118,
    LineOctaveMixed = 119,
    LineOctaveWithoutSpace = 120,
    LegatoSyntax = 121,
    LegatoEmpty = 122,
    LegatoRest = 123,
    LegatoNested = 124
}
