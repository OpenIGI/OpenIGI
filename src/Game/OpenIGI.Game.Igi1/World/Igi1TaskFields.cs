using OpenIGI.Game.World;
using OpenIGI.Scripting.Vocabulary;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// Every field a Project I.G.I. task type exports to level expressions, as each type's export handler in <c>IGI.exe</c> binds it.
/// </summary>
/// <remarks>
/// <para>
/// The first game's task types mostly load as declared placeholders, their variables bound by the systems that run them
/// during mission setup, so asking a scratch instance (<c>TaskFieldCatalog</c>) finds nothing. This table is the retail
/// truth instead. Every field name the executable binds is a <c>"%s.field"</c> format string (sixty-four of them); each is
/// used only by one type's export handler, which the type's registrar installs with <c>sub_401530</c> after naming the
/// type in <c>sub_401900</c>.
/// </para>
/// <para>
/// An export handler first runs its parent's (Generator's <c>0x43B6E0</c> calls ExplodeObject's through the handler table),
/// so a type exports its parent's fields too. That is why retail conditions read <c>SCameraControl_N.isOn</c>:
/// SCameraControl is registered under Terminal, which binds <c>isOn</c>. Each field's type is the binder the handler calls:
/// <c>is*</c> fields are bound as bytes (<c>sub_4B7EE0</c>), <c>n*</c> and <c>e*</c> as integers (<c>sub_4B7E80</c>), <c>v*</c> as
/// reals and <c>z*</c> as text, the original's naming following its binders throughout.
/// </para>
/// </remarks>
public static class Igi1TaskFields
{
    // Each type, the type it is registered under, its own fields in binding order, and the handler that binds them.
    private static readonly TaskFieldDeclaration[] Table =
    [
        new("ExplodeObject", null, ["isExploded"], 0x481F90),
        new("Generator", "ExplodeObject", ["isOn", "nBackupTimer"], 0x43B6E0),
        new("Radio", "ExplodeObject", ["isOn"], 0x43B1C0),
        new("Terminal", "ExplodeObject", ["isOn", "isInUse", "isLastInUse", "isHacked", "isHackedThisTick"], 0x443A60),
        new("AlarmControl", "Terminal", ["isTrigger", "isAlarm", "nTriggerLastTick"], 0x440A40),
        new("SCameraControl", "Terminal", ["isDetection"], 0x440EA0),
        new("Car", null, ["isExploded"], 0x42FCE0),
        new("SCamera", null, ["isExploded", "isDetection", "isLastDetection", "nDetectionTime"], 0x442890),
        new("AlarmLight", null, ["isOn", "isDestroyed"], 0x43A710),
        new("Switch", null, ["isOn", "isDestroyed", "isLastDestroyed", "isPressed", "isLastPressed", "isLastOn"], 0x445860),
        new("Siren", null, ["isOn", "isLastOn"], 0x449660),
        new("GenericTBA", "ExplodeObject", ["isOn", "isInUse", "isLastInUse", "isFinished", "isFinishedThisTick"], 0x4482D0),
        new("PlaceExplosiveTBA", "GenericTBA", [], 0x4388B0),
        new("Wire", null, ["isInUse", "isLastInUse"], 0x43F700),
        new("Cabinet", null, ["isInUse", "isLastInUse", "isSearched"], 0x4449C0),
        new("Door", null, ["isLocked", "isOpen", "isClosed", "isLastClosed", "isLastOpen", "isPicked", "nDoorOpenTicks"], 0x447A90),
        new("Elevator", null, ["vFloor", "vLastFloor", "nInactive", "nWantedFloor", "isMoving", "isStart", "isStop"], 0x43DF20),
        new("GuardGenerator", null, ["isOn", "isSpawned", "nSpawns"], 0x488430),
        new("EditVariable", null, ["nValue"], 0x488AD0),
        new("ConditionalContainer", null, ["isRun"], 0x487920),
        new("ConditionalSound", null, ["isRun", "isLastRun", "isPlaying", "nPlayTick"], 0x4253C0),
        new("AnimTask", null, ["isRun", "nTick"], 0x4F3BA0),
        new("CutScene", null, ["isRun", "isFinished", "nTick"], 0x4F4FB0),
        new("LevelTimer", null, ["isRun", "nTick"], 0x51BCC0),
        new("LevelFlow", null, ["isComplete", "isFailed", "nTick", "eDifficulty"], 0x488F90),
        new("StatusMessage", null, ["isSendt", "nTickSendt", "isFinishedDisplay", "nFinishedDisplay", "nTicksSinceFinishedDisplay"], 0x485770),
        new("AreaActivate", null, ["nActive"], 0x504530),
        new("Smoke", null, ["vGenerateFactor"], 0x437CA0),
        new("HumanAI", null, ["isDetection"], 0x450770),
        new("Human", null, ["isDead", "eTeam"], 0x460850),
        new("GenericPickup", null, ["isPickedUp"], 0x46C570),
        new("GunPickup", null, ["isPickedUp"], 0x46D170),
        new("AmmoPickup", null, ["isPickedUp"], 0x46D8D0),
        new("MoveRigidObj", null, ["vValue"], 0x51F980),
        new("NoiseQTask", null, ["vValue"], 0x5202F0),
        new("SmoothQTask", null, ["vRealValue", "vIdealValue"], 0x51F1F0),
        new("SlideBar", null, ["nValue", "vValue"], 0x4200C0),
        new("ScrollListBox", null, ["nActiveID"], 0x41C5A0),
        new("ListBox", null, ["nActiveID"], 0x41F1E0),
        new("InputBox", null, ["zData"], 0x41DB30),
        new("ToggleBox", null, ["isOn"], 0x41E5A0),
        new("ModelViewBox", null, ["isOn"], 0x422FC0),
        new("MenuScreen", null, ["nUserData"], 0x422190),
        new("ControlsMenu", null, ["isReset", "nOffset"], 0x423CB0),
    ];

    /// <summary>Gets each type's exported fields, its parent's first, by type name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<NativeParameter>> Fields { get; } = DeclaredTaskFields.Build(Table);
}
